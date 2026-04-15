namespace TravelGuide;

public partial class TouristLoginPage : ContentPage
{
    private readonly TouristAuthService _authService;

    public TouristLoginPage(TouristAuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        if (sender is Button loginBtn) loginBtn.IsEnabled = false;
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
            if (sender is Button loginButton) loginButton.IsEnabled = true;
        }
    }

    private async void OnGoRegisterClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(TouristRegisterPage));
    }
}
