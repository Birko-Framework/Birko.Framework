using System;
using System.Threading.Tasks;

namespace Birko.Communication.REST.Middleware
{
    /// <summary>
    /// Middleware factory for REST API authentication
    /// </summary>
    public static class RestAuthenticationMiddleware
    {
        /// <summary>
        /// Creates authentication middleware for the REST server
        /// </summary>
        /// <param name="authService">The REST authentication service</param>
        /// <returns>Middleware function</returns>
        public static Func<RestRequest, Func<Task<RestResponse>>, Task<RestResponse>> Create(
            RestAuthenticationService authService)
        {
            return async (request, next) =>
            {
                var token = authService.ExtractTokenFromRequest(request);
                var clientIp = authService.GetClientIpAddress(request);

                if (!authService.ValidateToken(token, clientIp))
                {
                    return RestResponse.Unauthorized("Invalid or missing authentication token, or IP address not allowed");
                }

                return await next().ConfigureAwait(false);
            };
        }
    }
}
