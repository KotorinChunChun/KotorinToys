using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace KotorinToys;
internal sealed record ReleasePackage(LocalPackage Package, long Length);
internal sealed record ReleaseCatalog(int SchemaVersion, ReleasePackage[] Packages);
