namespace KotorinToys;

internal sealed class SingleRowWheelGrid : DataGridView
{
    WheelDetents wheel;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
        int steps = wheel.Take(e.Delta);
        var direction = Math.Sign(steps);
        for (int i = 0; i < Math.Abs(steps); i++)
        {
            var first = FirstDisplayedScrollingRowIndex;
            if (first < 0) break;
            var next = direction > 0
                ? Rows.GetPreviousRow(first, DataGridViewElementStates.Visible, DataGridViewElementStates.Frozen)
                : Rows.GetNextRow(first, DataGridViewElementStates.Visible, DataGridViewElementStates.Frozen);
            if (next >= 0) FirstDisplayedScrollingRowIndex = next;
        }
    }
}

internal sealed class SingleRowWheelChecklist : CheckedListBox
{
    WheelDetents wheel;

    protected override void WndProc(ref Message message)
    {
        const int wmMouseWheel = 0x020a;
        if (message.Msg != wmMouseWheel) { base.WndProc(ref message); return; }
        int steps = wheel.Take(unchecked((short)(message.WParam.ToInt64() >> 16)));
        if (Items.Count > 0 && steps != 0)
            TopIndex = Math.Clamp(TopIndex - steps, 0, Items.Count - 1);
        message.Result = IntPtr.Zero;
    }
}

internal struct WheelDetents
{
    int remainder;

    internal int Take(int delta)
    {
        const int detent = 120;
        int total = delta + remainder;
        remainder = total % detent;
        return total / detent;
    }
}
