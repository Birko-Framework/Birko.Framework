using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Birko.Security.Authentication;
using Birko.Communication.REST;
using Birko.Communication.REST.Middleware;
using Birko.Communication.SOAP;
using Birko.Communication.SOAP.Middleware;
using RealRestClient = Birko.Communication.REST.RestClient;

namespace Birko.Framework.Examples.Communication
{
    /// <summary>
    /// Examples demonstrating REST and SOAP authentication with Microsoft.Extensions.Logging
    /// </summary>
    public static class AuthenticationExamples
    {
        /// <summary>
        /// Example: Using LoggerFactory (no DI container)
        /// </summary>
        public static async Task RestServerWithLogging()
        {
            // Create logger factory
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Create logger
            var logger = loggerFactory.CreateLogger<RestServer>();

            // Configure authentication
            var authConfig = new RestAuthenticationConfiguration
            {
                Enabled = true,
                Tokens = new List<string>
                {
                    "${API_TOKEN}", // Supports environment variables
                    "my-secret-token-123"
                },
                ApiKeyHeader = "X-API-Key",
                AllowQueryToken = true,
                QueryTokenName = "token"
            };

            var authLogger = loggerFactory.CreateLogger<RestAuthenticationService>();
            var authService = new RestAuthenticationService(authConfig, authLogger);

            // Create and configure server
            var server = new RestServer(logger);

            // Add authentication middleware
            server.UseMiddleware(RestAuthenticationMiddleware.Create(authService));

            // Register protected routes
            server.Get("/api/users", async (request) =>
            {
                logger.LogInformation("Getting users");
                return RestResponse.Ok("[{\"id\":1,\"name\":\"John\"}]");
            });

            await server.StartAsync("http://localhost:8080/api/");
        }

        /// <summary>
        /// Example: With dependency injection (ASP.NET Core style)
        /// </summary>
        public static void DependencyInjectionExample()
        {
            // In Program.cs or Startup.cs:
            /*
            services.AddSingleton<RestAuthenticationConfiguration>(sp =>
                new RestAuthenticationConfiguration
                {
                    Enabled = true,
                    Tokens = new List<string> { "${API_TOKEN}" }
                });

            services.AddSingleton<RestAuthenticationService>();
            services.AddSingleton<RestServer>();

            // Or use Microsoft.Extensions.Options pattern:
            services.Configure<RestAuthenticationConfiguration>(configuration.GetSection("Authentication"));
            services.AddSingleton<RestAuthenticationService>();
            */
        }

        /// <summary>
        /// Example: REST server with IP-bound tokens
        /// </summary>
        public static async Task RestServerWithIpBoundTokens()
        {
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

            var authConfig = new RestAuthenticationConfiguration
            {
                Enabled = true,
                TokenBindings = new List<TokenBinding>
                {
                    new TokenBinding
                    {
                        Token = "${SERVICE_A_TOKEN}",
                        AllowedIps = new List<string> { "192.168.1.100", "192.168.1.101" }
                    },
                    new TokenBinding
                    {
                        Token = "${SERVICE_B_TOKEN}",
                        AllowedIps = new List<string> { "10.0.0.50" }
                    }
                },
                ApiKeyHeader = "X-API-Key"
            };

            var authService = new RestAuthenticationService(
                authConfig,
                loggerFactory.CreateLogger<RestAuthenticationService>()
            );

            var server = new RestServer(loggerFactory.CreateLogger<RestServer>());
            server.UseMiddleware(RestAuthenticationMiddleware.Create(authService));

            server.Get("/api/data", async (request) =>
            {
                // Only requests from allowed IPs with valid tokens reach here
                return RestResponse.Ok("{\"data\":\"sensitive\"}");
            });

            await server.StartAsync("http://localhost:8080/api/");
        }

        /// <summary>
        /// Example: REST client with authentication
        /// </summary>
        public static async Task RestClientWithAuthentication()
        {
            var client = new RealRestClient("https://api.example.com");

            // Method 1: Using API Key header
            var headers1 = new Dictionary<string, string>
            {
                { "X-API-Key", "my-secret-token-123" }
            };

            var result1 = await client.GetAsync("/api/users", null, headers1);

            // Method 2: Using Authorization Bearer header
            var headers2 = new Dictionary<string, string>
            {
                { "Authorization", "Bearer my-secret-token-123" }
            };

            var result2 = await client.GetAsync("/api/users", null, headers2);

            // Method 3: Using query parameter (if enabled on server)
            var result3 = await client.GetAsync("/api/users?token=my-secret-token-123");

            // Method 4: Set default headers for all requests
            client.DefaultHeaders["X-API-Key"] = "my-secret-token-123";
            var result4 = await client.GetAsync("/api/users");
        }

        /// <summary>
        /// Example: SOAP service with authentication
        /// </summary>
        public static async Task SoapServiceWithAuthentication()
        {
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

            var authConfig = new SoapAuthenticationConfiguration
            {
                Enabled = true,
                Tokens = new List<string>
                {
                    "${SOAP_API_TOKEN}",
                    "soap-token-456"
                },
                SoapHeaderName = "Authentication",
                TokenElementName = "Token",
                AllowQueryToken = true,
                QueryTokenName = "token"
            };

            var authService = new SoapAuthenticationService(
                authConfig,
                loggerFactory.CreateLogger<SoapAuthenticationService>()
            );

            // Create authenticated SOAP service
            var service = new AuthenticatedSoapService(authService);
            var server = new SoapServer(loggerFactory.CreateLogger<SoapServer>());

            server.RegisterService("service.asmx", service);

            await server.StartAsync("http://localhost:8080/soap/", default);
        }

        /// <summary>
        /// Example: Authenticated SOAP service implementation
        /// </summary>
        public class AuthenticatedSoapService : SoapService
        {
            private readonly SoapAuthenticationService _authService;

            public AuthenticatedSoapService(SoapAuthenticationService authService)
            {
                _authService = authService;
            }

            public override async Task<string> ProcessRequestAsync(string soapEnvelope, CancellationToken cancellationToken = default)
            {
                // Note: In a real implementation, you'd need access to HttpListenerRequest
                // This would typically be done by modifying SoapServer to pass the request context
                // or by creating a custom SoapServer that supports authentication

                var token = _authService.ExtractTokenFromSoapEnvelope(soapEnvelope);

                // For demonstration, we'll just check the token
                // In production, you'd also check IP address via the request
                if (!_authService.ValidateToken(token, null))
                {
                    return SoapAuthenticationService.CreateAuthenticationFault("Invalid token");
                }

                // Process the request
                var action = ExtractSoapAction(soapEnvelope);
                var responseContent = ProcessAction(action, soapEnvelope);

                return CreateResponseEnvelope(responseContent);
            }

            private string ProcessAction(string? action, string envelope)
            {
                return action switch
                {
                    "GetData" => "<GetDataResponse><Result>Sample data</Result></GetDataResponse>",
                    "GetUser" => "<GetUserResponse><User><Id>1</Id><Name>John</Name></User></GetUserResponse>",
                    _ => throw new SoapException($"Unknown action: {action}")
                };
            }
        }

        /// <summary>
        /// Example: SOAP client with authentication header
        /// </summary>
        public static void SoapClientWithAuthentication()
        {
            var client = new SoapClient("https://soap.example.com/service.asmx");

            // Create SOAP envelope with authentication header
            var soapRequest = @"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Header>
    <Authentication>
      <Token>my-soap-token-456</Token>
    </Authentication>
  </soap:Header>
  <soap:Body>
    <GetData xmlns=""http://tempuri.org/"" />
  </soap:Body>
</soap:Envelope>";

            var response = client.SendRequest(
                "http://tempuri.org/GetData",
                soapRequest
            );

            ExampleOutput.WriteLine($"SOAP Response: {response}");
        }

        /// <summary>
        /// Custom exception for SOAP errors
        /// </summary>
        public class SoapException : Exception
        {
            public SoapException(string message) : base(message) { }
        }
    }
}
