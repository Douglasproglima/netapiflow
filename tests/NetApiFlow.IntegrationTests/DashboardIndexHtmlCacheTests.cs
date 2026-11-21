using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using NetApiFlow.Dashboard.StaticFiles;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class DashboardIndexHtmlCacheTests
{
    [Theory]
    [InlineData("<base href=\"/\">")]
    [InlineData("<base href=\"/\"/>")]
    [InlineData("<base href=\"/\" />")]
    [InlineData("<base  href = \"/\"  >")]
    [InlineData("<BASE HREF=\"/\">")]
    [InlineData("<base href='/'>")]
    public void GetHtml_WhateverWayTheBuildWritesTheBaseTag_PointsItAtTheDashboardPath(string originalTag)
    {
        var cache = CacheFor($"<head>{originalTag}<title>x</title></head>", new CapturingLogger());

        var html = cache.GetHtml("/app/flow/");

        html.ShouldBe("<head><base href=\"/app/flow/\"><title>x</title></head>");
    }

    [Fact]
    public void GetHtml_WithoutAnyBaseTag_ServesTheFileAsItIsAndWarnsWhatWasMissing()
    {
        var logger = new CapturingLogger();
        var cache = CacheFor("<head><title>x</title></head>", logger);

        var html = cache.GetHtml("/flow/");

        html.ShouldBe("<head><title>x</title></head>");
        logger.Warnings.ShouldHaveSingleItem().ShouldContain("<base href=\"/\">");
    }

    [Fact]
    public void GetHtml_AskedTwiceForTheSamePath_WarnsOnlyOnce()
    {
        var logger = new CapturingLogger();
        var cache = CacheFor("<head></head>", logger);

        cache.GetHtml("/flow/");
        cache.GetHtml("/flow/");

        logger.Warnings.Count.ShouldBe(1);
    }

    private static DashboardIndexHtmlCache CacheFor(string indexHtml, ILogger logger) =>
        new(new SingleFileProvider("index.html", indexHtml), logger);

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    private sealed class SingleFileProvider(string name, string content) : IFileProvider
    {
        public IFileInfo GetFileInfo(string subpath) =>
            subpath == name ? new StringFileInfo(name, content) : new NotFoundFileInfo(subpath);

        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

        public Microsoft.Extensions.Primitives.IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }

    private sealed class StringFileInfo(string name, string content) : IFileInfo
    {
        public bool Exists => true;

        public long Length => Encoding.UTF8.GetByteCount(content);

        public string? PhysicalPath => null;

        public string Name => name;

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public bool IsDirectory => false;

        public Stream CreateReadStream() => new MemoryStream(Encoding.UTF8.GetBytes(content));
    }
}
