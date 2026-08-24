using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using API.Middleware;
using DAL.Exceptions;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Serilog;
using Xunit;

namespace API.Tests.Middleware;

[TestSubject(typeof(EntityScopeViolationMiddleware))]
public class EntityScopeViolationMiddlewareTest
{
    private static readonly ILogger Logger = new LoggerConfiguration().CreateLogger();

    private static DefaultHttpContext ContextWithBodyBuffer()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return new StreamReader(context.Response.Body).ReadToEnd();
    }

    [Fact]
    public async Task TestInvokeAsyncPassesThroughWhenNothingThrows()
    {
        var context = ContextWithBodyBuffer();
        var called = false;

        var middleware = new EntityScopeViolationMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, Logger);

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task TestInvokeAsyncTurnsAScopeViolationIntoForbidden()
    {
        var context = ContextWithBodyBuffer();

        var middleware = new EntityScopeViolationMiddleware(
            _ => throw new EntityScopeViolationException("Risk", 7, "entities [1, 2]"), Logger);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
    }

    [Fact]
    public async Task TestInvokeAsyncWritesTheViolationAsJson()
    {
        var context = ContextWithBodyBuffer();

        var middleware = new EntityScopeViolationMiddleware(
            _ => throw new EntityScopeViolationException("Risk", 7, "entities [1, 2]"), Logger);

        await middleware.InvokeAsync(context);

        using var payload = JsonDocument.Parse(ReadBody(context));

        Assert.Equal("entity_scope_violation", payload.RootElement.GetProperty("error").GetString());
        Assert.Contains("Risk", payload.RootElement.GetProperty("message").GetString());
        Assert.Contains("7", payload.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task TestInvokeAsyncReportsAnUnassignedEntityId()
    {
        var context = ContextWithBodyBuffer();

        var middleware = new EntityScopeViolationMiddleware(
            _ => throw new EntityScopeViolationException("Mitigation", null, "entities [3]"), Logger);

        await middleware.InvokeAsync(context);

        using var payload = JsonDocument.Parse(ReadBody(context));

        Assert.Contains("<unassigned>", payload.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task TestInvokeAsyncLetsOtherExceptionsThrough()
    {
        var context = ContextWithBodyBuffer();

        var middleware = new EntityScopeViolationMiddleware(
            _ => throw new InvalidOperationException("unrelated"), Logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task TestInvokeAsyncRethrowsOnceTheResponseHasStarted()
    {
        // Rewriting the status line is impossible at that point, so the middleware must rethrow
        // rather than corrupt a response already on the wire. DefaultHttpContext never flips
        // HasStarted on its own, so the branch needs a response feature that reports it.
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        var middleware = new EntityScopeViolationMiddleware(
            _ => throw new EntityScopeViolationException("Risk", 7, "entities [1]"), Logger);

        await Assert.ThrowsAsync<EntityScopeViolationException>(() => middleware.InvokeAsync(context));
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state) { }

        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
