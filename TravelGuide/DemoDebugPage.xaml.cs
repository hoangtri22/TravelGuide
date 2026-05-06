namespace TravelGuide;

public partial class DemoDebugPage : ContentPage
{
    public DemoDebugPage()
    {
        InitializeComponent();
        RefreshData();
    }

    private void OnRefreshClicked(object? sender, EventArgs e) => RefreshData();

    private void RefreshData()
    {
        var apiBase = EndpointResolver.ResolveApiBaseUrl();
        var adminBase = EndpointResolver.ResolveAdminWebBaseUrls().Primary;
        var last = DemoDiagnostics.GetLastHttpError();

        ApiBaseLabel.Text = string.IsNullOrWhiteSpace(apiBase) ? "N/A" : apiBase;
        AdminBaseLabel.Text = string.IsNullOrWhiteSpace(adminBase) ? "N/A" : adminBase;
        LastHttpErrorLabel.Text = string.IsNullOrWhiteSpace(last.Message) ? "N/A" : last.Message;
        LastHttpErrorAtLabel.Text = last.AtUtc.HasValue
            ? last.AtUtc.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
            : "N/A";
    }
}
