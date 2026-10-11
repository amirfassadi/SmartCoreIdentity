using System.Security.Cryptography.X509Certificates;
using SmartCore.Identity;

namespace SmartCore.Identity.Bff.Host;

public sealed record HostConfiguration(string Database, Uri IdentityOrigin, string ClientKey, string ReceiptKey,
    Secrets SessionSecrets, string KeyRing, X509Certificate2 WrappingCertificate)
{
    public static HostConfiguration Load(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            throw new InvalidOperationException("Browser BFF deployment is disabled until the B1 acceptance and release gates pass.");
        string Required(string key) => configuration[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Missing configuration: {key}.");
        var origin = new Uri(Required("Bff:IdentityOrigin"), UriKind.Absolute);
        if (!origin.IsLoopback || origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0
            || origin.AbsolutePath != "/" || origin.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("This B1 scaffold requires a private loopback Identity origin.");
        var urls = Required("urls").Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (urls.Length == 0 || urls.Any(value => !Uri.TryCreate(value, UriKind.Absolute, out var url)
            || !url.IsLoopback || url.Scheme is not ("http" or "https") || url.AbsolutePath != "/"))
            throw new InvalidOperationException("This B1 scaffold may listen only on loopback URLs.");
        var client = Required("Bff:ClientKey"); var receipt = Required("Bff:ReceiptKey");
        if (client.Length < 32 || receipt.Length < 32 || client == receipt)
            throw new InvalidOperationException("Separate BFF client and refresh receipt keys are required.");
        var session = new Secrets(Required("Bff:SessionMacKey"), Required("Bff:SessionMaterialKey"));
        var ring = Required("Bff:DataProtection:KeyRing");
        var certificatePath = Required("Bff:DataProtection:CertificatePath");
        if (!Path.IsPathFullyQualified(ring) || !Directory.Exists(ring)
            || !Path.IsPathFullyQualified(certificatePath) || !File.Exists(certificatePath))
            throw new InvalidOperationException("Provision an absolute shared key ring and a separate wrapping certificate before startup.");
        var canonicalRing = Path.GetFullPath(ring).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(certificatePath).StartsWith(canonicalRing, StringComparison.Ordinal))
            throw new InvalidOperationException("Wrapping certificate must be outside the key ring.");
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath,
            Required("Bff:DataProtection:CertificatePassword"), X509KeyStorageFlags.EphemeralKeySet);
        using var rsa = certificate.GetRSAPrivateKey();
        var now = DateTime.UtcNow;
        if (!certificate.HasPrivateKey || rsa is null || certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now)
        {certificate.Dispose(); throw new InvalidOperationException("A currently valid RSA certificate with a private key is required.");}
        return new(Required("ConnectionStrings:Bff"), origin, client, receipt, session, ring, certificate);
    }
}
