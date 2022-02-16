using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace KyoshinMonitorProxy
{
	public class CertManager
	{
		private const int CURRENT_CERT_VERSION = 1;

		static X509Certificate2 CreateSelfSignedCertificateBasedOnCertificateAuthorityPrivateKey(string subjectName, string issuerName, AsymmetricKeyParameter issuerPrivKey)
		{
			const int keyStrength = 2048;

			// Generating Random Numbers
			var randomGenerator = new CryptoApiRandomGenerator();
			var random = new SecureRandom(randomGenerator);
			var signatureFactory = new Asn1SignatureFactory("SHA512WITHRSA", issuerPrivKey, random);
			// The Certificate Generator
			var certificateGenerator = new X509V3CertificateGenerator();
			certificateGenerator.AddExtension(X509Extensions.ExtendedKeyUsage, true, new ExtendedKeyUsage(new[] { new DerObjectIdentifier("1.3.6.1.5.5.7.3.1"), new DerObjectIdentifier("1.3.6.1.5.5.7.3.2") }));
			certificateGenerator.AddExtension(X509Extensions.SubjectAlternativeName, true, new GeneralNames(new[] {
				new GeneralName(GeneralName.DnsName, "*.bosai.go.jp"),
				new GeneralName(GeneralName.DnsName, "*.lmoni.bosai.go.jp"),
				new GeneralName(GeneralName.DnsName, "*.kmoni.bosai.go.jp"),
			}));

			// Serial Number
			var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random);
			certificateGenerator.SetSerialNumber(serialNumber);

			// Issuer and Subject Name
			certificateGenerator.SetIssuerDN(new X509Name(issuerName));
			certificateGenerator.SetSubjectDN(new X509Name(subjectName));

			// Valid For
			DateTime notBefore = DateTime.UtcNow.Date;
			DateTime notAfter = notBefore.AddYears(2);

			certificateGenerator.SetNotBefore(notBefore);
			certificateGenerator.SetNotAfter(notAfter);

			// Subject Public Key
			AsymmetricCipherKeyPair subjectKeyPair;
			var keyGenerationParameters = new KeyGenerationParameters(random, keyStrength);
			var keyPairGenerator = new RsaKeyPairGenerator();
			keyPairGenerator.Init(keyGenerationParameters);
			subjectKeyPair = keyPairGenerator.GenerateKeyPair();

			certificateGenerator.SetPublicKey(subjectKeyPair.Public);

			var certificate = certificateGenerator.Generate(signatureFactory);

			// correcponding private key
			var info = PrivateKeyInfoFactory.CreatePrivateKeyInfo(subjectKeyPair.Private);

			// merge into X509Certificate2
			var x509 = new X509Certificate2(certificate.GetEncoded(), (string?)null, X509KeyStorageFlags.PersistKeySet);

			var seq = (Asn1Sequence)Asn1Object.FromByteArray(info.ParsePrivateKey().GetDerEncoded());

			var rsa = RsaPrivateKeyStructure.GetInstance(seq);
			var rsaparams = new RsaPrivateCrtKeyParameters(
				rsa.Modulus, rsa.PublicExponent, rsa.PrivateExponent, rsa.Prime1, rsa.Prime2, rsa.Exponent1, rsa.Exponent2, rsa.Coefficient);

			return x509.CopyWithPrivateKey(DotNetUtilities.ToRSA(rsaparams));

		}
		static X509Certificate2 CreateCertificateAuthorityCertificate(string subjectName)
		{
			const int keyStrength = 2048;

			// Generating Random Numbers
			var randomGenerator = new CryptoApiRandomGenerator();
			var random = new SecureRandom(randomGenerator);

			// The Certificate Generator
			var certificateGenerator = new X509V3CertificateGenerator();

			// Serial Number
			certificateGenerator.SetSerialNumber(BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random));

			// Issuer and Subject Name
			var subjectDN = new X509Name(subjectName);
			certificateGenerator.SetSubjectDN(subjectDN);
			var issuerDN = subjectDN;
			certificateGenerator.SetIssuerDN(issuerDN);

			// Valid For
			var notBefore = DateTime.UtcNow.Date;
			certificateGenerator.SetNotBefore(notBefore);

			var notAfter = notBefore.AddYears(2);
			certificateGenerator.SetNotAfter(notAfter);

			// Subject Public Key
			var keyGenerationParameters = new KeyGenerationParameters(random, keyStrength);
			var keyPairGenerator = new RsaKeyPairGenerator();
			keyPairGenerator.Init(keyGenerationParameters);
			var subjectKeyPair = keyPairGenerator.GenerateKeyPair();

			certificateGenerator.SetPublicKey(subjectKeyPair.Public);

			// Generating the Certificate
			var signatureFactory = new Asn1SignatureFactory("SHA512WITHRSA", subjectKeyPair.Private, random);
			// selfsign certificate
			var certificate = certificateGenerator.Generate(signatureFactory);

			// correcponding private key
			var info = PrivateKeyInfoFactory.CreatePrivateKeyInfo(subjectKeyPair.Private);

			// merge into X509Certificate2
			var x509 = new X509Certificate2(certificate.GetEncoded(), (string?)null, X509KeyStorageFlags.PersistKeySet);

			var seq = (Asn1Sequence)Asn1Object.FromByteArray(info.ParsePrivateKey().GetDerEncoded());

			var rsa = RsaPrivateKeyStructure.GetInstance(seq);
			var rsaparams = new RsaPrivateCrtKeyParameters(
				rsa.Modulus, rsa.PublicExponent, rsa.PrivateExponent, rsa.Prime1, rsa.Prime2, rsa.Exponent1, rsa.Exponent2, rsa.Coefficient);

			return x509.CopyWithPrivateKey(DotNetUtilities.ToRSA(rsaparams));
		}

		static void AddCertificateToSpecifiedStore(X509Certificate2 cert, StoreName st, StoreLocation sl)
		{
			using var store = new X509Store(st, sl);
			store.Open(OpenFlags.ReadWrite);
			store.Add(cert);
			store.Close();
		}

		static X509Certificate2? GetCertificateToSpecifiedStore(string thumbPrint, StoreName st, StoreLocation sl)
		{
			using var store = new X509Store(st, sl);
			store.Open(OpenFlags.ReadOnly);
			try
			{
				return store.Certificates.FirstOrDefault(c => c.Thumbprint == thumbPrint);
			}
			finally
			{
				store.Close();
			}
		}

		static void RemoveCertificateToSpecifiedStore(string thumbPrint, StoreName st, StoreLocation sl)
		{
			using var store = new X509Store(st, sl);
			store.Open(OpenFlags.ReadWrite);
			if (store.Certificates.FirstOrDefault(c => c.Thumbprint == thumbPrint) is X509Certificate2 cert)
				store.Remove(cert);
			store.Close();
		}

		static AsymmetricKeyParameter TransformRSAPrivateKey(RSA? privateKey)
		{
			var parameters = privateKey?.ExportParameters(true) ?? throw new Exception("証明書の詳細が取得できません");

			return new RsaPrivateCrtKeyParameters(
				new BigInteger(1, parameters.Modulus),
				new BigInteger(1, parameters.Exponent),
				new BigInteger(1, parameters.D),
				new BigInteger(1, parameters.P),
				new BigInteger(1, parameters.Q),
				new BigInteger(1, parameters.DP),
				new BigInteger(1, parameters.DQ),
				new BigInteger(1, parameters.InverseQ));
		}

		public static X509Certificate2 CreateOrGetCert(Settings config)
		{
			var certSubjectName = "*.bosai.go.jp";
			var subjectName = "CN=KyoshinMonitorProxy";
			var isRootRecreated = false;

			if (
#if DEBUG
				true ||
#endif
				config.RootThumbprint == null ||
				GetCertificateToSpecifiedStore(config.RootThumbprint, StoreName.Root, StoreLocation.LocalMachine) is not X509Certificate2 root ||
				config.Version is not int ver ||
				ver < CURRENT_CERT_VERSION
			)
			{
				// 既存の証明書を削除する
				if (config.RootThumbprint != null)
					RemoveCertificateToSpecifiedStore(config.RootThumbprint, StoreName.Root, StoreLocation.LocalMachine);

				Console.WriteLine("ルート証明書を作成しています");
				root = CreateCertificateAuthorityCertificate(subjectName);

				config.RootThumbprint = root.Thumbprint;
				isRootRecreated = true;
			}

			if (
				isRootRecreated || config.PersonalThumbprint == null ||
				GetCertificateToSpecifiedStore(config.PersonalThumbprint, StoreName.My, StoreLocation.LocalMachine) is not X509Certificate2 cert
			)
			{
				// ルート証明書が更新されていた場合、既存の証明書を削除する
				if (isRootRecreated && config.PersonalThumbprint != null)
					RemoveCertificateToSpecifiedStore(config.PersonalThumbprint, StoreName.My, StoreLocation.LocalMachine);

				Console.WriteLine("証明書を作成しています");
				cert = CreateSelfSignedCertificateBasedOnCertificateAuthorityPrivateKey(
					"CN=" + certSubjectName,
					subjectName,
					TransformRSAPrivateKey(root.GetRSAPrivateKey())
				);

				Console.WriteLine("証明書をストアに登録しています");
				if (isRootRecreated)
					AddCertificateToSpecifiedStore(root, StoreName.Root, StoreLocation.LocalMachine);
				AddCertificateToSpecifiedStore(cert, StoreName.My, StoreLocation.LocalMachine);

				config.PersonalThumbprint = cert.Thumbprint;
			}

			return cert;
		}

		public static void RemoveCert(Settings config)
		{
			if (config.RootThumbprint != null)
				RemoveCertificateToSpecifiedStore(config.RootThumbprint, StoreName.Root, StoreLocation.LocalMachine);
			if (config.PersonalThumbprint != null)
				RemoveCertificateToSpecifiedStore(config.PersonalThumbprint, StoreName.My, StoreLocation.LocalMachine);
		}
	}
}
