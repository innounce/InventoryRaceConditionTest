using System.Net;
using System.Text.Json;
using Inventory.Api.Dtos;
using Inventory.Api.Exceptions;

namespace Inventory.Api.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (statusCode, errorCode, message) = ex switch
            {
                ProductNotFoundException e => (HttpStatusCode.NotFound, e.ErrorCode, e.Message),
                InsufficientStockException e => (HttpStatusCode.BadRequest, e.ErrorCode, e.Message),
                InvalidQuantityException e => (HttpStatusCode.BadRequest, e.ErrorCode, e.Message),
                _ => (HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "系統發生未預期的錯誤")
            };

            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new ErrorResponse(errorCode, message),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
    }
}
