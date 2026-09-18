using System.Threading.Tasks;

namespace Birko.Communication.SSE.Middleware
{
    /// <summary>
    /// Authentication middleware for SSE connections
    /// </summary>
    public class SseAuthenticationMiddleware : ISseMiddleware
    {
        private readonly SseAuthenticationService _authService;

        /// <summary>
        /// Initializes a new instance of the SseAuthenticationMiddleware class
        /// </summary>
        public SseAuthenticationMiddleware(SseAuthenticationService authService)
        {
            _authService = authService;
        }

        /// <summary>
        /// Creates a new instance of the middleware
        /// </summary>
        public static SseAuthenticationMiddleware Create(SseAuthenticationService authService)
        {
            return new SseAuthenticationMiddleware(authService);
        }

        /// <summary>
        /// Processes the SSE connection request through authentication
        /// </summary>
        public async Task<SseResponse?> ProcessAsync(SseContext context, SseRequestDelegate next)
        {
            var result = _authService.AuthenticateConnection(
                context.Headers,
                context.QueryString,
                context.RemoteEndPoint);

            if (!result.IsAuthenticated)
            {
                return SseResponse.Denied(result.StatusCode ?? 401, result.ErrorMessage);
            }

            // Store client ID for later use
            context.ClientId = result.ClientId;

            // Continue to next middleware
            return await next(context);
        }
    }
}
