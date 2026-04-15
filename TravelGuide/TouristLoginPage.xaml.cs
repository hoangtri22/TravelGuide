namespace TravelGuide;

public partial class TouristLoginPage : ContentPage
{
    private readonly TouristAuthService _authService;
    private bool _isSubmitting;

    public TouristLoginPage(TouristAuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        await TryLoginAsync(sender as Button);
    }

    private async void OnGoRegisterClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(TouristRegisterPage));
    }

    private void OnUsernameCompleted(object? sender, EventArgs e)
    {
        PasswordEntry.Focus();
    }

    private async void OnPasswordCompleted(object? sender, EventArgs e)
    {
        await TryLoginAsync();
    }

    private async Task TryLoginAsync(Button? loginButton = null)
    {
        if (_isSubmitting) return;
        _isSubmitting = true;
        if (loginButton != null) loginButton.IsEnabled = false;
        try
        {
            var username = (UsernameEntry.Text ?? "").Trim();
            var password = PasswordEntry.Text ?? "";
            if (username.Length < 3 || password.Length < 6)
            {
                await DisplayAlert("Notice", "Invalid username or password format.", "OK");
                return;
            }

            var (ok, message) = await _authService.LoginAsync(username, password);
            if (!ok)
            {
                await DisplayAlert("Sign In Failed", message, "OK");
                return;
            }

            await Shell.Current.GoToAsync(nameof(HomePage));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Sign In Failed", $"Unexpected error: {ex.Message}", "OK");
        }
        finally
        {
            _isSubmitting = false;
            if (loginButton != null) loginButton.IsEnabled = true;
        }
    }

}
