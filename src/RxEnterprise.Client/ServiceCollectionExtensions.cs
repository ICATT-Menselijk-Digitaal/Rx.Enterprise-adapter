using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;

namespace RxEnterprise.Client;

public static class ServiceCollectionExtensions
{
    public static IHttpClientBuilder AddRxEnterpriseClient(
        this IServiceCollection services,
        Action<RxEnterpriseClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RxEnterpriseClientOptions();
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            throw new InvalidOperationException("RxEnterpriseClientOptions.BaseUrl must be set.");

        var baseUrl = options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/";

        return services
            .AddHttpClient<IRxEnterpriseClient, RxEnterpriseClient>(client =>
            {
                client.BaseAddress = new Uri(baseUrl);
                client.Timeout = options.Timeout;
                client.DefaultRequestVersion = HttpVersion.Version11;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var certPath = Resolve(options.CertificatePath);
                var keyPath = Resolve(options.PrivateKeyPath);
                var cert = X509Certificate2.CreateFromPemFile(certPath, keyPath);

                return new SocketsHttpHandler
                {
                    ConnectCallback = async (context, ct) =>
                    {
                        // Force IPv4: the default dual-stack socket tries IPv6 first,
                        // which hangs when the server has no working IPv6 path.
                        var addrs = await Dns.GetHostAddressesAsync(
                            context.DnsEndPoint.Host, AddressFamily.InterNetwork, ct);

                        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                        await socket.ConnectAsync(new IPEndPoint(addrs[0], context.DnsEndPoint.Port), ct);

                        var sslStream = new SslStream(new NetworkStream(socket, ownsSocket: true));
                        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                        {
                            TargetHost = context.DnsEndPoint.Host,
                            ClientCertificates = [cert],
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        }, ct);

                        return sslStream;
                    }
                };
            });
    }

    private static string Resolve(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        var fromCwd = Path.Combine(Directory.GetCurrentDirectory(), path);
        return File.Exists(fromCwd) ? fromCwd : Path.Combine(AppContext.BaseDirectory, path);
    }
}
