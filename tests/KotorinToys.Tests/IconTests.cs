using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using KotorinToys;

internal static class IconTests
{
    public static void Asset(Action<bool, string> check)
    {
        using var icon = AppIcon.Create();
        check(icon.Size == new Size(32, 32), "本体トレイの専用アイコン寸法");
        using var bitmap = icon.ToBitmap();
        var pixels = Enumerable.Range(0, 32).SelectMany(y => Enumerable.Range(0, 32).Select(x => bitmap.GetPixel(x, y))).Where(p => p.A >= 128).ToArray();
        check(pixels.Any(p => p.B > p.R * 1.3 && p.G > p.R) && pixels.Any(p => p.R > p.G * 1.3 && p.R > p.B * 1.3) &&
            pixels.Any(p => p.G > p.R * 1.3 && p.G > p.B * 1.3), "本体トレイは04の青い鳥と赤・緑の積み木");
        check(bitmap.GetPixel(0, 0).A == 0 && bitmap.GetPixel(31, 31).A == 0, "本体Iconの透明背景");
        foreach (var size in new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128 })
        {
            using var resized = new Icon(icon, size, size);
            check(resized.Size == new Size(size, size), $"埋込みIconから{size}pxを選択できる");
        }
    }

    public static void FormsAndDefinitions(string root, string fixtures, Action<bool, string> check)
    {
        var source = Path.Combine(root, "src", "KotorinToys", "assets", "KotorinToys.ico");
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(AppIcon.ResourceName)!;
        using var copy = new MemoryStream(); stream.CopyTo(copy);
        check(copy.ToArray().SequenceEqual(File.ReadAllBytes(source)), "管理リソースと共通ICOが全フレーム一致");
        foreach (var project in new[] { "KotorinToys", "KotorinToys.Maintenance" })
        {
            var file = Path.Combine(root, "src", project, project + ".csproj");
            var path = XDocument.Load(file).Descendants("ApplicationIcon").Single().Value;
            check(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, path)) == source, project + "のEXE埋込みが共通ICOを参照");
        }
        var installer = File.ReadAllText(Path.Combine(root, "installers", "product.iss"));
        check(installer.Replace("\r\n", "\n").Contains("#if ProductName == \"KotorinToys\"\nSetupIconFile=..\\src\\KotorinToys\\assets\\KotorinToys.ico\n#endif"), "本体インストーラーだけ04を指定・子のブランドを保持");
        check(File.Exists(Path.GetFullPath(Path.Combine(root, "installers", @"..\src\KotorinToys\assets\KotorinToys.ico"))), "SetupIconFileの相対パスを解決");
        check(installer.Contains("UninstallDisplayIcon={app}\\{#ProductName}.exe,0") &&
            installer.Contains("IconFilename: \"{app}\\{#ProductName}.exe\"; IconIndex: 0"), "削除一覧とショートカットは各製品EXEの先頭アイコン");
        var store = new HostSettingsStore(Path.Combine(fixtures, "icon-settings"));
        var coordinator = new StartupCoordinator(store, new UnusedGateway(), new(root), Environment.ProcessPath!);
        using var expected = AppIcon.Create();
        using var dashboard = new Dashboard(root, store, coordinator);
        using var installation = new InstallationForm(new(root));
        using var startup = new StartupSettingsForm(store, coordinator, ToolCatalog.Load(root));
        foreach (var form in new Form[] { dashboard, installation, startup })
        {
            check(form is BrandedForm && form.ShowIcon && form.ShowInTaskbar && Same(form.Icon!, expected), form.GetType().Name + "のタイトルバー・タスクバーへ04を割当");
        }
        check(!ReferenceEquals(dashboard.Icon, installation.Icon) && !ReferenceEquals(installation.Icon, startup.Icon), "画面ごとに独立したIconを所有");
        dashboard.Dispose(); dashboard.Dispose();
        check(dashboard.IsDisposed && Same(installation.Icon!, expected) && Same(startup.Icon!, expected), "二重破棄しても別画面のアイコンが有効");
        installation.Dispose(); installation.Dispose(); startup.Dispose(); startup.Dispose();
        check(installation.IsDisposed && startup.IsDisposed, "全独自画面のアイコンと画面を安全に破棄");
        var formTypes = typeof(AppIcon).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(Form).IsAssignableFrom(t));
        check(formTypes.All(t => typeof(BrandedForm).IsAssignableFrom(t)), "新しい独自Formも共通アイコン規約を満たす");
        check(!Directory.Exists(store.DirectoryPath), "アイコン検証だけで設定・履歴を生成しない");
    }

    static bool Same(Icon actual, Icon expected)
    {
        using var a = actual.ToBitmap(); using var b = expected.ToBitmap();
        return a.Size == b.Size && Enumerable.Range(0, a.Height).All(y => Enumerable.Range(0, a.Width).All(x => a.GetPixel(x, y) == b.GetPixel(x, y)));
    }

    public static void Executable(string executable, string source, Action<bool, string> check)
    {
        var path = Path.GetFullPath(executable); var label = Path.GetFileName(path);
        // データとして読み込むだけで、EXEの実行や初期化は行わない。
        var module = LoadLibraryEx(path, 0, 0x02 | 0x20);
        if (module == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            byte[]? group = null; int groups = 0; Exception? callbackError = null;
            EnumResourceName callback = (handle, type, name, parameter) =>
            {
                try { groups++; group = Read(handle, type, name); return true; }
                catch (Exception ex) { callbackError = ex; return false; }
            };
            bool enumerated = EnumResourceNames(module, 14, callback, 0);
            if (callbackError != null) throw new IOException("PEアイコン資源を読めません。", callbackError);
            if (!enumerated) throw new Win32Exception(Marshal.GetLastWin32Error());
            var ico = File.ReadAllBytes(source);
            int count = BitConverter.ToUInt16(ico, 4);
            check(groups == 1 && group != null && group.Length == 6 + 14 * count && BitConverter.ToUInt16(group, 4) == count,
                label + "のPEアイコングループに全10サイズを埋込み");
            for (int i = 0; i < count; i++)
            {
                int entry = 6 + 16 * i, native = 6 + 14 * i;
                int offset = checked((int)BitConverter.ToUInt32(ico, entry + 12)), length = checked((int)BitConverter.ToUInt32(ico, entry + 8));
                var payload = Read(module, 3, BitConverter.ToUInt16(group!, native + 12));
                int size = ico[entry] == 0 ? 256 : ico[entry];
                check(group!.AsSpan(native, 12).SequenceEqual(ico.AsSpan(entry, 12)) && payload.AsSpan().SequenceEqual(ico.AsSpan(offset, length)),
                    label + $"のPE内{size}pxが元ICOとバイト一致");
            }
            GC.KeepAlive(callback);
        }
        finally { FreeLibrary(module); }
    }

    static byte[] Read(nint module, nint type, nint name)
    {
        var resource = FindResource(module, name, type);
        if (resource == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        uint length = SizeofResource(module, resource);
        var pointer = LockResource(LoadResource(module, resource));
        if (pointer == 0 || length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var bytes = new byte[checked((int)length)]; Marshal.Copy(pointer, bytes, 0, bytes.Length); return bytes;
    }

    sealed class UnusedGateway : IStartupGateway
    {
        public ScheduledTaskState? Read(string name) => throw new InvalidOperationException("Icon試験でSchedulerへアクセスしない。");
        public ScheduledTaskState Create(TaskPlan plan) => throw new NotSupportedException();
        public ScheduledTaskState Restore(string name, string xml, string securityDescriptor = "") => throw new NotSupportedException();
        public void Delete(string name, string expectedFingerprint) => throw new NotSupportedException();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    delegate bool EnumResourceName(nint module, nint type, nint name, nint parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW", SetLastError = true)]
    static extern nint LoadLibraryEx(string path, nint file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumResourceNamesW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumResourceNames(nint module, nint type, EnumResourceName callback, nint parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindResourceW", SetLastError = true)]
    static extern nint FindResource(nint module, nint name, nint type);
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint LoadResource(nint module, nint resource);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint SizeofResource(nint module, nint resource);
    [DllImport("kernel32.dll")] static extern nint LockResource(nint resource);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool FreeLibrary(nint module);
}
