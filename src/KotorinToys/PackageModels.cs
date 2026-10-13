using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace KotorinToys;

internal sealed record LocalPackage(string Id, string Name, string Version, string AppId, string File, string Sha256, string Signature);
internal sealed record PackageCatalog(int SchemaVersion, string DistributionStatus, LocalPackage[] Packages);
internal sealed record InstallResult(string Id, string Operation, string State, string Detail, int? ExitCode = null);
