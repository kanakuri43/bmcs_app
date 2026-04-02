using Prism.Commands;
using Prism.Mvvm;
using bmcs_app.Core.Models;

namespace bmcs_app.Closing.ViewModels;

public class ClosingMainViewModel : BindableBase
{
    public InvoiceClosingViewModel InvoiceTab { get; }
    public ArClosingViewModel      ArTab      { get; }

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { SetProperty(ref _selectedTabIndex, value); RaisePropertyChanged(nameof(StatusMessage)); }
    }

    public string StatusMessage =>
        SelectedTabIndex == 0 ? InvoiceTab.StatusMessage : ArTab.StatusMessage;

    // F キー → アクティブタブへ委譲
    public DelegateCommand AggregateCommand         { get; }
    public DelegateCommand CancelAggregationCommand { get; }
    public DelegateCommand PrintCommand             { get; }

    public ClosingMainViewModel(IEnumerable<Customer> customers)
    {
        var closingDays = customers.Select(c => c.ClosingDay);

        InvoiceTab = new InvoiceClosingViewModel(closingDays);
        ArTab      = new ArClosingViewModel();

        AggregateCommand = new DelegateCommand(() =>
        {
            if (SelectedTabIndex == 0) InvoiceTab.AggregateCommand.Execute();
            else                        ArTab.AggregateCommand.Execute();
        });

        CancelAggregationCommand = new DelegateCommand(() =>
        {
            if (SelectedTabIndex == 0) InvoiceTab.CancelAggregationCommand.Execute();
            else                        ArTab.CancelAggregationCommand.Execute();
        });

        PrintCommand = new DelegateCommand(() => InvoiceTab.PrintCommand.Execute());

        InvoiceTab.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(InvoiceTab.StatusMessage)) RaisePropertyChanged(nameof(StatusMessage)); };
        ArTab.PropertyChanged      += (_, e) => { if (e.PropertyName == nameof(ArTab.StatusMessage))      RaisePropertyChanged(nameof(StatusMessage)); };
    }
}
