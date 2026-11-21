using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

public sealed class InMemoryFiles(string name, string content) : IFileProvider
{
    public IFileInfo GetFileInfo(string subpath) =>
        subpath.TrimStart('/').Equals(name, StringComparison.OrdinalIgnoreCase)
            ? new InMemoryFile(name, content)
            : new NotFoundFileInfo(subpath);

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    private sealed class InMemoryFile(string fileName, string text) : IFileInfo
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(text);

        public bool Exists => true;

        public long Length => _bytes.Length;

        public string? PhysicalPath => null;

        public string Name => fileName;

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public bool IsDirectory => false;

        public Stream CreateReadStream() => new MemoryStream(_bytes);
    }
}
