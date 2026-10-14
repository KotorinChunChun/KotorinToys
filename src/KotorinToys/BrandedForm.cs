namespace KotorinToys;

// 本体の独自画面はすべて継承し、アイコンの割当と所有権を揃える。
internal abstract class BrandedForm : Form
{
    readonly Icon appIcon = AppIcon.Create();
    bool iconDisposed;

    protected BrandedForm() => Icon = appIcon;

    protected override void Dispose(bool disposing)
    {
        try { base.Dispose(disposing); }
        finally
        {
            if (disposing && !iconDisposed)
            {
                iconDisposed = true;
                appIcon.Dispose();
            }
        }
    }
}
