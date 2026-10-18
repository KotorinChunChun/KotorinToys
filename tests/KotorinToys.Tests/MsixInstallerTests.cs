using KotorinToys;

internal static class MsixInstallerTests
{
    sealed class Deployment(Action<string> add, Action<string> remove) : IMsixDeployment
    {
        public Task Install(string file) { add(file); return Task.CompletedTask; }
        public Task Remove(string name) { remove(name); return Task.CompletedTask; }
    }
    public static async Task Run(string root, Action<bool, string> check)
    {
        var fixture = MsixPackageTests.Fixture(root); var tool = MsixReleaseTests.Tool;
        string packages = Path.Combine(root, "user-packages"), backup = Path.Combine(root, "private-backup");
        string state = Path.Combine(packages, MsixPolicy.Family, "LocalState"); Directory.CreateDirectory(state);
        File.WriteAllText(Path.Combine(state, "HttpSelector.db"), "設定・履歴 fixture");
        File.WriteAllText(Path.Combine(state, "integrity-key.bin"), "dummy-key");
        var store = new MsixDataStore(packages, backup);
        var installed = new InstallationState("導入済み", "0.0.3.2", "fixture", "HttpSelector.exe");
        int adds = 0, removes = 0, stops = 0, reservations = 0, recoveries = 0;
        bool failRemove = false, failAdd = false, refuseStop = false, damageData = false, failReservation = false;
        var deployment = new Deployment(_ => {
            adds++; if (failAdd) throw new IOException("OS導入失敗");
            installed = installed with { State = "導入済み", Version = "0.0.3.3" };
            if (damageData) File.WriteAllText(Path.Combine(state, "HttpSelector.db"), "changed");
        }, name => {
            check(name == "HttpSelector.Development_0.0.3.3_x64__xtxrnffqrdk4w", "削除対象は照合済みの完全なMSIX登録名だけ");
            removes++; if (failRemove) throw new IOException("OS削除失敗");
            Directory.Delete(state, true); installed = new("未導入", null, null, null);
        });
        var installer = new MsixInstaller(_ => installed, _ => { stops++; return Task.FromResult(new ToolState(refuseStop ? "実行中" : "停止中")); },
            deployment, store, _ => { reservations++; return new(!failReservation, "予約解除"); }, _ => { recoveries++; return new(true, "予約復元"); }, MsixPackageTests.Trusted);
        var result = await installer.Install(tool, fixture.Catalog);
        check(result.State == "成功" && adds == 1 && File.ReadAllText(Path.Combine(state, "HttpSelector.db")) == "設定・履歴 fixture", "MSIX更新は同じFamilyのDB・設定を保持");
        failRemove = true;
        result = await installer.Uninstall(tool);
        check(result.State == "失敗" && recoveries == 1 && File.Exists(Path.Combine(state, "HttpSelector.db")), "OS削除失敗時はデータ退避を保持して管理予約を復元");
        failRemove = false;
        result = await installer.Uninstall(tool);
        check(result.State == "成功" && !Directory.Exists(state) && removes == 2 && reservations == 2, "MSIX削除前にデータ退避と予約整理を完了");
        using (store.AcquireLease()) check(store.Pending()?.Files.Length == 2, "削除後もFamily・所有者・hash付きの保持記録が残る");
        result = await installer.Install(tool, fixture.Catalog);
        check(result.State == "成功" && File.ReadAllText(Path.Combine(state, "HttpSelector.db")) == "設定・履歴 fixture" &&
            File.ReadAllText(Path.Combine(state, "integrity-key.bin")) == "dummy-key", "再導入でDBと整合性キーを起動前に復元");
        using (store.AcquireLease()) check(store.Pending() == null, "復元完了後は未復元マーカーを完了へ移す");
        using (var locked = new FileStream(Path.Combine(state, "HttpSelector.db"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = await installer.Uninstall(tool);
            check(result.State == "失敗" && removes == 2 && reservations == 2, "データ退避不能なら予約変更もMSIX削除も行わない");
        }
        int before = stops;
        var bad = new MsixInstaller(_ => installed, _ => { stops++; return Task.FromResult(new ToolState("停止中")); }, deployment, store,
            _ => new(true, ""), _ => new(true, ""), _ => throw new IOException("未信頼署名"));
        result = await bad.Install(tool, fixture.Catalog);
        check(result.State == "失敗" && stops == before && adds == 2, "署名失敗では正常停止もOS展開も行わない");
        refuseStop = true; result = await installer.Uninstall(tool);
        check(result.State == "失敗" && removes == 2, "正常停止拒否ではMSIX削除へ進まない");
        refuseStop = false; failReservation = true; result = await installer.Uninstall(tool);
        check(result.State == "失敗" && removes == 2, "予約整理失敗ではMSIX削除へ進まない");
        failReservation = false; failAdd = true; result = await installer.Install(tool, fixture.Catalog);
        check(result.State == "失敗", "OS展開エラーを成功にしない"); failAdd = false;
        installed = installed with { Version = "0.0.3.4" }; before = stops;
        result = await installer.Install(tool, fixture.Catalog);
        check(result.State == "失敗" && stops == before, "MSIXダウングレードは停止前に拒否");
        installed = installed with { Version = "0.0.3.2" }; damageData = true;
        result = await installer.Install(tool, fixture.Catalog);
        check(result.State == "要確認" && result.Detail.Contains("保持照合"), "展開後にデータが変わった場合は成功・自動起動にしない");
        using (store.AcquireLease())
        {
            var snapshot = store.Capture(); store.Retain(snapshot);
            File.WriteAllText(Path.Combine(state, "HttpSelector.db"), "別の新しい履歴");
            bool denied = false; try { store.PreflightRestore(snapshot); } catch (IOException) { denied = true; }
            check(denied && File.ReadAllText(Path.Combine(state, "HttpSelector.db")) == "別の新しい履歴", "復元先の新しい履歴を上書きしない");
            denied = false; try { store.VerifyPreserved(snapshot with { Family = "Other" }); } catch (IOException) { denied = true; }
            check(denied, "データを別Familyへ復元しない");
            denied = false; try { store.VerifyPreserved(snapshot with { Owner = "Other" }); } catch (IOException) { denied = true; }
            check(denied, "データを別ユーザーへ復元しない");
        }
        check(MsixDeployment.Failure(new System.Runtime.InteropServices.COMException("使用中", unchecked((int)0x80073D02))).Contains("正常終了"), "MSIX使用中のHRESULTと次の操作を表示");
        int updateStops = 0, restarts = 0, validations = 0;
        bool invalidSignature = true;
        var coordinator = new ToolUpdateCoordinator(_ => new("更新が必要", "0.0.3.2", "fixture", "HttpSelector.exe"),
            _ => Task.FromResult(new ToolState("実行中", LaunchOrigin: "via_kotorintoys")),
            _ => { updateStops++; return Task.FromResult(new ToolState("停止中")); },
            (_, origin) => { restarts++; check(origin == "via_kotorintoys", "MSIX更新後も元の起動経路を保持"); return Task.FromResult(new ToolState("実行中")); },
            (_, _, _) => Task.FromResult(new InstallResult(tool.Id, "install", "要確認", "復元確認待ち")),
            _ => { validations++; if (invalidSignature) throw new IOException("未信頼署名"); return MsixPackageTests.Trusted(""); });
        result = await coordinator.Run(tool, fixture.Catalog);
        check(result.State == "失敗" && updateStops == 0 && validations == 1, "更新Coordinatorも署名検証を正常停止より前に行う");
        invalidSignature = false;
        result = await coordinator.Run(tool, fixture.Catalog);
        check(result.State == "要確認" && updateStops == 1 && restarts == 0, "更新が必要のMSIXも導入対象・データ要確認なら自動起動しない");
    }
}
