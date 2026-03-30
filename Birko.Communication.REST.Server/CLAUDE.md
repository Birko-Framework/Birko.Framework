# Birko.Communication.REST.Server

## Overview
REST API server implementation using HttpListener for hosting RESTful APIs.

## Project Location
`C:\Source\Birko.Communication.REST.Server\`

## Purpose
- HTTP/HTTPS server hosting
- Route registration and request routing
- Middleware pipeline (authentication)
- Request/response handling

## Components

### Server
- `RestServer` - HttpListener-based REST server with route registration, middleware pipeline, CORS, and static file serving

### Middleware
- `RestAuthenticationConfiguration` - Authentication configuration
- `RestAuthenticationMiddleware` - Authentication middleware
- `RestAuthenticationService` - Authentication service

## Dependencies
- Birko.Communication
- Microsoft.Extensions.Logging
- System.Net.HttpListener

## Related Projects
- **Birko.Communication.REST** - REST API client (complementary project)

## Maintenance

### CLAUDE.md Updates
When making major changes, update this CLAUDE.md to reflect new or renamed files, changed architecture, or updated dependencies.
