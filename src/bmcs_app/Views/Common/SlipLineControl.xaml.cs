using System.Windows.Controls;
using bmcs_app.ViewModels.Common;

namespace bmcs_app.Views.Common;

/// <summary>伝票明細行の1行分（TODO.md 4-2）。</summary>
public partial class SlipLineControl : UserControl
{
    public SlipLineControl()
    {
        InitializeComponent();

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is SlipLineViewModel oldLine)
            {
                oldLine.MoveToQuantityRequested -= OnMoveToQuantityRequested;
            }

            if (e.NewValue is SlipLineViewModel newLine)
            {
                newLine.MoveToQuantityRequested += OnMoveToQuantityRequested;
            }
        };
    }

    private void OnMoveToQuantityRequested() => QuantityBox.Focus();
}
