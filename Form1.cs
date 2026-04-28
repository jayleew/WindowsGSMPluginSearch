using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsGsmPluginSearch;

public partial class Form1 : Form
{
    private const string GitHubClientId = "Ov23liEwnQLo0CM17TBy";
    private const string DeviceFlowGrantType = "urn:ietf:params:oauth:grant-type:device_code";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly TextBox searchTextBox = new();
    private readonly Button searchButton = new();
    private readonly Button signInButton = new();
    private readonly ListView resultsListView = new();
    private readonly Label statusLabel = new();

    public Form1()
    {
        InitializeComponent();
        BuildInterface();
    }

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WindowsGsmPluginSearch", "1.0"));
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GH_TOKEN");

        ApplyAuthorizationHeader(httpClient, token);

        return httpClient;
    }

    private static void ApplyAuthorizationHeader(HttpClient httpClient, string? token)
    {
        httpClient.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
    }

    private void BuildInterface()
    {
        Text = "WindowsGSM Plugin Search";
        MinimumSize = new Size(760, 420);
        StartPosition = FormStartPosition.CenterScreen;

        var searchPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            ColumnCount = 3,
            Padding = new Padding(12, 12, 12, 4)
        };
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

        searchTextBox.Dock = DockStyle.Fill;
        searchTextBox.PlaceholderText = "Search WindowsGSM plugins...";
        searchTextBox.KeyDown += SearchTextBox_KeyDown;

        searchButton.Text = "Search";
        searchButton.Dock = DockStyle.Fill;
        searchButton.Click += SearchButton_Click;
        searchButton.Enabled = HasGitHubToken();

        signInButton.Text = "Sign in with GitHub";
        signInButton.Dock = DockStyle.Fill;
        signInButton.Click += SignInButton_Click;

        searchPanel.Controls.Add(searchTextBox, 0, 0);
        searchPanel.Controls.Add(searchButton, 1, 0);
        searchPanel.Controls.Add(signInButton, 2, 0);

        resultsListView.Dock = DockStyle.Fill;
        resultsListView.FullRowSelect = true;
        resultsListView.GridLines = true;
        resultsListView.HideSelection = false;
        resultsListView.MultiSelect = false;
        resultsListView.View = View.Details;
        resultsListView.Columns.Add("Repository", 240);
        resultsListView.Columns.Add("Description", 390);
        resultsListView.Columns.Add("Stars", 70, HorizontalAlignment.Right);
        resultsListView.ItemActivate += ResultsListView_ItemActivate;
        resultsListView.Click += ResultsListView_Click;

        statusLabel.Dock = DockStyle.Bottom;
        statusLabel.Height = 28;
        statusLabel.Padding = new Padding(12, 5, 12, 0);
        statusLabel.Text = HasGitHubToken()
            ? "Enter a search term and click Search."
            : "Sign in with GitHub to enable search.";

        Controls.Add(resultsListView);
        Controls.Add(statusLabel);
        Controls.Add(searchPanel);
    }

    private async void SearchButton_Click(object? sender, EventArgs e)
    {
        await SearchAsync();
    }

    private async void SignInButton_Click(object? sender, EventArgs e)
    {
        await SignInWithGitHubAsync();
    }

    private async void SearchTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.SuppressKeyPress = true;
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (!HasGitHubToken())
        {
            MessageBox.Show("Sign in with GitHub before searching.", "Sign-in required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var searchTerm = searchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            MessageBox.Show("Enter a search term first.", "Search required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            searchTextBox.Focus();
            return;
        }

        SetSearchingState(true);
        resultsListView.Items.Clear();

        try
        {
            var encodedSearchTerm = Uri.EscapeDataString(searchTerm);
            var url = $"https://api.github.com/search/repositories?q=WindowsGSM+{encodedSearchTerm}+plugin+in:name,description,readme";
            using var response = await HttpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                await ShowGitHubErrorAsync(response);
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            var searchResponse = await JsonSerializer.DeserializeAsync<GitHubSearchResponse>(stream);
            var repositories = searchResponse?.Items ?? [];

            foreach (var repository in repositories)
            {
                var item = new ListViewItem(repository.FullName);
                item.SubItems.Add(string.IsNullOrWhiteSpace(repository.Description) ? "(No description)" : repository.Description);
                item.SubItems.Add(repository.StargazersCount.ToString("N0"));
                item.Tag = repository.HtmlUrl;
                resultsListView.Items.Add(item);
            }

            statusLabel.Text = repositories.Count == 0
                ? "No repositories found."
                : $"Found {repositories.Count:N0} repositories. Click a result to open it on GitHub.";
        }
        catch (HttpRequestException ex)
        {
            statusLabel.Text = "Could not reach GitHub.";
            MessageBox.Show(ex.Message, "Network error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (JsonException ex)
        {
            statusLabel.Text = "GitHub returned an unexpected response.";
            MessageBox.Show(ex.Message, "Response error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetSearchingState(false);
        }
    }

    private void SetSearchingState(bool isSearching)
    {
        searchButton.Enabled = !isSearching && HasGitHubToken();
        searchTextBox.Enabled = !isSearching;
        signInButton.Enabled = !isSearching;
        statusLabel.Text = isSearching ? "Searching GitHub..." : statusLabel.Text;
        Cursor = isSearching ? Cursors.WaitCursor : Cursors.Default;
    }

    private static bool HasGitHubToken()
    {
        return HttpClient.DefaultRequestHeaders.Authorization is not null;
    }

    private async Task SignInWithGitHubAsync()
    {
        if (string.IsNullOrWhiteSpace(GitHubClientId))
        {
            MessageBox.Show(
                "Create a GitHub OAuth App, enable Device Flow, then paste its Client ID into the GitHubClientId constant in Form1.cs.",
                "GitHub Client ID required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        SetSignInState(true, "Requesting GitHub login code...");

        try
        {
            var deviceCode = await RequestDeviceCodeAsync();

            Process.Start(new ProcessStartInfo
            {
                FileName = deviceCode.VerificationUri,
                UseShellExecute = true
            });

            ShowDeviceCodeDialog(deviceCode.UserCode);

            statusLabel.Text = "Waiting for GitHub authorization...";

            var token = await PollForAccessTokenAsync(deviceCode);
            ApplyAuthorizationHeader(HttpClient, token);
            statusLabel.Text = "Signed in with GitHub. Searches will use authenticated API limits.";
        }
        catch (OperationCanceledException ex)
        {
            statusLabel.Text = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            statusLabel.Text = "Could not reach GitHub sign-in.";
            MessageBox.Show(ex.Message, "GitHub sign-in error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (JsonException ex)
        {
            statusLabel.Text = "GitHub returned an unexpected sign-in response.";
            MessageBox.Show(ex.Message, "GitHub sign-in error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetSignInState(false, statusLabel.Text);
        }
    }

    private void SetSignInState(bool isSigningIn, string status)
    {
        signInButton.Enabled = !isSigningIn;
        searchButton.Enabled = !isSigningIn && HasGitHubToken();
        searchTextBox.Enabled = !isSigningIn;
        statusLabel.Text = status;
        Cursor = isSigningIn ? Cursors.WaitCursor : Cursors.Default;
    }

    private void ShowDeviceCodeDialog(string userCode)
    {
        using var dialog = new Form
        {
            Text = "GitHub sign in",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(360, 170)
        };

        var instructionLabel = new Label
        {
            AutoSize = false,
            Text = "Enter this code in the browser:",
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(16, 16),
            Size = new Size(328, 24)
        };

        var codeTextBox = new TextBox
        {
            ReadOnly = true,
            Text = userCode,
            TextAlign = HorizontalAlignment.Center,
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            Location = new Point(16, 48),
            Size = new Size(328, 34)
        };

        var copyButton = new Button
        {
            Text = "Copy code",
            Location = new Point(132, 116),
            Size = new Size(100, 32)
        };
        copyButton.Click += (_, _) =>
        {
            Clipboard.SetText(userCode);
            copyButton.Text = "Copied";
        };

        var closeButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(244, 116),
            Size = new Size(100, 32)
        };

        dialog.Controls.Add(instructionLabel);
        dialog.Controls.Add(codeTextBox);
        dialog.Controls.Add(copyButton);
        dialog.Controls.Add(closeButton);
        dialog.AcceptButton = closeButton;
        dialog.CancelButton = closeButton;

        dialog.ShowDialog(this);
    }

    private static async Task<GitHubDeviceCodeResponse> RequestDeviceCodeAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/device/code");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("client_id", GitHubClientId)
        ]);

        using var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<GitHubDeviceCodeResponse>(stream)
            ?? throw new JsonException("GitHub returned an empty device-code response.");
    }

    private static async Task<string> PollForAccessTokenAsync(GitHubDeviceCodeResponse deviceCode)
    {
        var interval = Math.Max(deviceCode.Interval, 5);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(deviceCode.ExpiresIn);

        while (DateTimeOffset.UtcNow < expiresAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval));

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("client_id", GitHubClientId),
                new KeyValuePair<string, string>("device_code", deviceCode.DeviceCode),
                new KeyValuePair<string, string>("grant_type", DeviceFlowGrantType)
            ]);

            using var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            var tokenResponse = await JsonSerializer.DeserializeAsync<GitHubTokenResponse>(stream)
                ?? throw new JsonException("GitHub returned an empty token response.");

            if (!string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                return tokenResponse.AccessToken;
            }

            switch (tokenResponse.Error)
            {
                case "authorization_pending":
                    break;
                case "slow_down":
                    interval += 5;
                    break;
                case "expired_token":
                    throw new OperationCanceledException("GitHub sign-in code expired. Start sign-in again.");
                case "access_denied":
                    throw new OperationCanceledException("GitHub sign-in was canceled.");
                default:
                    throw new OperationCanceledException(tokenResponse.ErrorDescription ?? "GitHub sign-in failed.");
            }
        }

        throw new OperationCanceledException("GitHub sign-in code expired. Start sign-in again.");
    }

    private async Task ShowGitHubErrorAsync(HttpResponseMessage response)
    {
        var error = await TryReadGitHubErrorAsync(response);

        if (response.StatusCode == HttpStatusCode.Forbidden &&
            error?.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true)
        {
            statusLabel.Text = "GitHub API rate limit exceeded. Sign in with GitHub and try again.";
            MessageBox.Show(
                "GitHub has rate-limited this request. Sign in with GitHub and try again.",
                "GitHub rate limit exceeded",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        statusLabel.Text = string.IsNullOrWhiteSpace(error?.Message)
            ? $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}."
            : $"GitHub returned {(int)response.StatusCode}: {error.Message}";
    }

    private static async Task<GitHubErrorResponse?> TryReadGitHubErrorAsync(HttpResponseMessage response)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<GitHubErrorResponse>(stream);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void ResultsListView_Click(object? sender, EventArgs e)
    {
        OpenSelectedRepository();
    }

    private void ResultsListView_ItemActivate(object? sender, EventArgs e)
    {
        OpenSelectedRepository();
    }

    private void OpenSelectedRepository()
    {
        if (resultsListView.SelectedItems.Count == 0 ||
            resultsListView.SelectedItems[0].Tag is not string url)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private sealed record GitHubSearchResponse(
        [property: JsonPropertyName("items")] List<GitHubRepository> Items);

    private sealed record GitHubRepository(
        [property: JsonPropertyName("full_name")] string FullName,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("stargazers_count")] int StargazersCount);

    private sealed record GitHubErrorResponse(
        [property: JsonPropertyName("message")] string Message);

    private sealed record GitHubDeviceCodeResponse(
        [property: JsonPropertyName("device_code")] string DeviceCode,
        [property: JsonPropertyName("user_code")] string UserCode,
        [property: JsonPropertyName("verification_uri")] string VerificationUri,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("interval")] int Interval);

    private sealed record GitHubTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
}
