using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using backend.Models;
using backend.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace backend.Middleware
{
    /// <summary>
    /// 集中式例外處理：把未攔截的例外統一包成 <see cref="ResultViewModel{T}"/>，
    /// 並依例外型別決定 HTTP status code，避免每支商家 API 各自判斷一次。
    /// </summary>
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (ConflictException ex)
            {
                _logger.LogWarning(ex, "商業邏輯衝突：{Message}", ex.Message);
                await WriteResultAsync(context, HttpStatusCode.Conflict, new ResultViewModel<object>
                {
                    isSuccess = false,
                    message = ex.Message,
                    Result = ex.Conflicts
                });
            }
            catch (NotFoundException ex)
            {
                await WriteResultAsync(context, HttpStatusCode.NotFound, new ResultViewModel<object>
                {
                    isSuccess = false,
                    message = ex.Message,
                    Result = null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "未攔截的例外");
                await WriteResultAsync(context, HttpStatusCode.InternalServerError, new ResultViewModel<object>
                {
                    isSuccess = false,
                    message = ex.Message,
                    Result = null
                });
            }
        }

        private static Task WriteResultAsync(HttpContext context, HttpStatusCode statusCode, ResultViewModel<object> body)
        {
            if (context.Response.HasStarted)
            {
                return Task.CompletedTask;
            }

            context.Response.Clear();
            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            return context.Response.WriteAsync(JsonSerializer.Serialize(body));
        }
    }
}
