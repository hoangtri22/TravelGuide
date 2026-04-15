namespace TravelGuide;

public partial class TouristRegisterPage : ContentPage
{
    private readonly TouristAuthService _authService;

    public TouristRegisterPage(TouristAuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        if (sender is Button registerBtn) registerBtn.IsEnabled = false;
        try
        {
            var username = (UsernameEntry.Text ?? "").Trim();
            var displayName = (DisplayNameEntry.Text ?? "").Trim();
            var password = PasswordEntry.Text ?? "";
            const string tier = "free";

            if (username.Length < 3 || displayName.Length < 2 || password.Length < 6)
            {
                await DisplayAlert("Notice", "Please enter valid sign-up information.", "OK");
                return;
            }

            var (ok, message) = await _authService.RegisterAsync(username, password, displayName, tier);
            if (!ok)
            {
                await DisplayAlert("Sign Up Failed", message, "OK");
                return;
            }

            await DisplayAlert("Success", "Your account was created. Please sign in.", "OK");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Sign Up Failed", $"Unexpected error: {ex.Message}", "OK");
        }
        finally
        {
            if (sender is Button button) button.IsEnabled = true;
        }
    }

    private async void OnBackToLoginTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
