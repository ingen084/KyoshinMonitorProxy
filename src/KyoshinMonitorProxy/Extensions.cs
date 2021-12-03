using System.Net.Http.Headers;

namespace KyoshinMonitorProxy
{
	public static class Extensions
	{
		public static HttpRequestMessage CreateProxiedHttpRequest(this HttpContext context, string uriString)
		{
			var request = context.Request;

			var requestMessage = new HttpRequestMessage();
			var requestMethod = request.Method;
			var usesStreamContent = true; // When using other content types, they specify the Content-Type header, and may also change the Content-Length.

			// Write to request content, when necessary.
			if (!HttpMethods.IsGet(requestMethod) &&
				!HttpMethods.IsHead(requestMethod) &&
				!HttpMethods.IsDelete(requestMethod) &&
				!HttpMethods.IsTrace(requestMethod))
			{
				if (request.HasFormContentType)
				{
					usesStreamContent = false;
					requestMessage.Content = request.Form.ToHttpContent(request.ContentType);
				}
				else
				{
					requestMessage.Content = new StreamContent(request.Body);
				}
			}

			// Copy the request headers.
			foreach (var header in request.Headers)
			{
				if (
					header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
					header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
					header.Key.Equals("KeepAlive", StringComparison.OrdinalIgnoreCase) ||
					header.Key.Equals("Close", StringComparison.OrdinalIgnoreCase) ||
					!usesStreamContent && (
						header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
						header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
					)
				)
					continue;
				if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
					requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
			}

			// Set destination and method.
			requestMessage.Headers.Host = context.Request.Headers.Host;
			requestMessage.RequestUri = new Uri(uriString + context.Request.Path);
			requestMessage.Method = new HttpMethod(requestMethod);

			return requestMessage;
		}
		public static Task WriteProxiedHttpResponseAsync(this HttpContext context, HttpResponseMessage responseMessage)
		{
			var response = context.Response;

			response.StatusCode = (int)responseMessage.StatusCode;
			foreach (var header in responseMessage.Headers)
				response.Headers[header.Key] = header.Value.ToArray();

			foreach (var header in responseMessage.Content.Headers)
				response.Headers[header.Key] = header.Value.ToArray();

			response.Headers.Remove("Transfer-Encoding");
			response.Headers.Remove("Connection");
			response.Headers.Remove("KeepAlive");
			response.Headers.Remove("Close");

			return responseMessage.Content.CopyToAsync(response.Body);
		}
		internal static HttpContent ToHttpContent(this IFormCollection collection, string contentTypeHeader)
		{
			// @PreferLinux:
			// Form content types resource: https://stackoverflow.com/questions/4526273/what-does-enctype-multipart-form-data-mean/28380690
			// There are three possible form content types:
			// - text/plain, which should never be used and this does not handle (a request with that will not have IsFormContentType true anyway)
			// - application/x-www-form-urlencoded, which doesn't handle file uploads and escapes any special characters
			// - multipart/form-data, which does handle files and doesn't require any escaping, but is quite bulky for short data (due to using some content headers for each value, and a boundary sequence between them)

			// A single form element can have multiple values. When sending them they are handled as separate items with the same name, not a singe item with multiple values.
			// For example, a=1&a=2.

			var contentType = MediaTypeHeaderValue.Parse(contentTypeHeader);

			if (contentType.MediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) // specification: https://url.spec.whatwg.org/#concept-urlencoded
				return new FormUrlEncodedContent(collection.SelectMany(formItemList => formItemList.Value.Select(value => new KeyValuePair<string, string>(formItemList.Key, value))));

			if (!contentType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
				throw new Exception($"Unknown form content type `{contentType.MediaType}`.");

			// multipart/form-data specification https://tools.ietf.org/html/rfc7578
			// It has each value separated by a boundary sequence, which is specified in the Content-Type header.
			// As a proxy it is probably best to reuse the boundary used in the original request as it is not necessarily random.
			var delimiter = contentType.Parameters.Single(p => p.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase)).Value.Trim('"');

			var multipart = new MultipartFormDataContent(delimiter);
			foreach (var formVal in collection)
			{
				foreach (var value in formVal.Value)
					multipart.Add(new StringContent(value), formVal.Key);
			}
			foreach (var file in collection.Files)
			{
				var content = new StreamContent(file.OpenReadStream());
				foreach (var header in file.Headers)
					content.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string>)header.Value);
				multipart.Add(content, file.Name, file.FileName);
			}
			return multipart;
		}
	}
}
