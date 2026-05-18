using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace RxEnterpriseZgwProxy.Auth;

public static class AuthExtensions
{
    public static void AddZgwAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddAuthentication()
            .AddJwtBearer("zgw", opts =>
            {
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeyResolver = (_, token, _, _) => GetSigningKey(configuration, token),
                    ValidateIssuer = true,
                    ValidIssuers = GetCredentials(configuration).Select(x => x.Id),
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    LifetimeValidator = (_, expires, _, _) =>
                        expires.HasValue && expires - DateTime.UtcNow < TimeSpan.FromHours(1),
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
            });

        services
            .AddAuthorizationBuilder()
            .AddFallbackPolicy("zgw", p =>
                p.RequireClaim("client_id")
                 .RequireClaim("user_id")
                 .RequireClaim("user_representation"));
    }

    private static IEnumerable<SecurityKey> GetSigningKey(IConfiguration configuration, SecurityToken token)
    {
        var key = GetCredentials(configuration)
            .Where(x => x.Id == token.Issuer)
            .Select(x => x.Secret)
            .Select(Encoding.UTF8.GetBytes)
            .Select(b => new SymmetricSecurityKey(b))
            .FirstOrDefault();

        yield return key ?? throw new Exception($"No signing key found for issuer '{token.Issuer}'");
    }

    private static IEnumerable<ClientCredential> GetCredentials(IConfiguration configuration) =>
        configuration.GetSection("CLIENTS").Get<IEnumerable<ClientCredential>>()
        ?? [];

    private record ClientCredential(string Id, string Secret);
}
