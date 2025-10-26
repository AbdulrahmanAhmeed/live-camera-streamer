using System.Net.Http;
using System.Threading.Tasks;

namespace IpCamera
{
    public partial class MainPage : ContentPage
    {
        private HttpClient _httpClient;
        private bool _isConnected = false;
        private CancellationTokenSource _cancellationTokenSource;

        public MainPage()
        {
            InitializeComponent();
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30); // Increased timeout
        }

        private async void OnConnectClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(IpAddressEntry.Text))
            {
                StatusLabel.Text = "Please enter a valid camera URL";
                return;
            }

            try
            {
                ConnectButton.IsEnabled = false;
                StatusLabel.Text = "Connecting...";

                var url = IpAddressEntry.Text.Trim();

                // Validate URL format
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    StatusLabel.Text = "Invalid URL format";
                    ConnectButton.IsEnabled = true;
                    return;
                }

                // Handle different stream types
                if (uri.Scheme == "rtsp")
                {
                    _ = ConnectToRTSPCamera(url);
                }
                else if (uri.Scheme == "http" || uri.Scheme == "https")
                {
                    await ConnectToHttpCamera(url);
                }
                else
                {
                    StatusLabel.Text = "Unsupported protocol. Use http://, https://, or rtsp://";
                    ConnectButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
                ConnectButton.IsEnabled = true;
            }
        }

        private Task ConnectToRTSPCamera(string url)
        {
            try
            {
                NoVideoLabel.IsVisible = false;
                VlcButtonGrid.IsVisible = true;
                ShowRTSPFallback(url);

                _isConnected = true;
                ConnectButton.IsEnabled = false;
                DisconnectButton.IsEnabled = true;
                StatusLabel.Text = $"RTSP stream ready: {url}";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Failed to connect to RTSP stream: {ex.Message}";
                ConnectButton.IsEnabled = true;
            }

            return Task.CompletedTask;
        }

        private void ShowRTSPFallback(string url)
        {
            try
            {
                VideoWebView.IsVisible = true;
                VideoImage.IsVisible = false;

                var vlcInstalled = FindVlcExecutable() != null;
                var vlcStatus = vlcInstalled ? "✅ VLC is installed" : "❌ VLC not found";

                var htmlContent = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <style>
                        body {{ margin: 0; padding: 0; background: black; display: flex; flex-direction: column; justify-content: center; align-items: center; height: 100vh; color: white; font-family: Arial, sans-serif; }}
                        .info {{ text-align: center; padding: 20px; max-width: 700px; }}
                        .url {{ background: #333; padding: 10px; border-radius: 5px; margin: 10px 0; word-break: break-all; font-family: monospace; }}
                        .suggestions {{ margin-top: 20px; text-align: left; }}
                        .suggestion {{ margin: 10px 0; padding: 15px; background: #222; border-radius: 5px; border-left: 4px solid #007acc; }}
                        .download {{ background: #1a472a; border-left-color: #28a745; }}
                        .status {{ padding: 10px; border-radius: 5px; margin: 10px 0; }}
                        .status.installed {{ background: #1a472a; }}
                        .status.not-installed {{ background: #721c24; }}
                        .copy-btn {{ background: #007acc; color: white; border: none; padding: 5px 10px; border-radius: 3px; cursor: pointer; margin-left: 10px; }}
                    </style>
                </head>
                <body>
                    <div class='info'>
                        <h2>RTSP Stream</h2>
                        <p><strong>Stream URL:</strong></p>
                        <div class='url' id='streamUrl'>{url}</div>
                        <button class='copy-btn' onclick='copyUrl()'>Copy URL</button>
                        
                        <div class='status {(vlcInstalled ? "installed" : "not-installed")}'>
                            <strong>VLC Status:</strong> {vlcStatus}
                        </div>
                        
                        <div class='suggestions'>
                            <h3>How to View This Stream:</h3>
                            
                            <div class='suggestion download'>
                                <strong>1. Use VLC Media Player (Recommended):</strong><br>
                                - Click 'Open in VLC' button above<br>
                                - Or: VLC → Media → Open Network Stream → Paste URL
                            </div>
                            
                            <div class='suggestion'>
                                <strong>2. Test in Browser:</strong><br>
                                - Click 'Test in Browser' button to check if the stream is accessible
                            </div>
                        </div>
                    </div>
                    
                    <script>
                        function copyUrl() {{
                            navigator.clipboard.writeText('{url}').then(function() {{
                                alert('URL copied to clipboard!');
                            }});
                        }}
                    </script>
                </body>
                </html>";

                VideoWebView.Source = new HtmlWebViewSource { Html = htmlContent };
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error showing fallback: {ex.Message}";
            }
        }

        private async Task ConnectToHttpCamera(string url)
        {
            try
            {
                NoVideoLabel.IsVisible = false;
                VlcButtonGrid.IsVisible = true;

                StatusLabel.Text = "Step 1: Testing network connectivity...";
                await Task.Delay(100); // Allow UI to update

                // Test the connection with detailed diagnostics
                try
                {
                    // First try to ping/test basic connectivity
                    StatusLabel.Text = $"Step 2: Attempting to connect to {url}...";

                    using var testClient = new HttpClient();
                    testClient.Timeout = TimeSpan.FromSeconds(15);

                    // Add user agent to avoid some camera rejections
                    testClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 IP Camera Viewer");

                    var response = await testClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                    StatusLabel.Text = $"Step 3: Response received! Status: {response.StatusCode}, Type: {contentType}";

                    if (response.IsSuccessStatusCode)
                    {
                        StatusLabel.Text = "Step 4: Loading stream in viewer...";

                        // Use WebView for better MJPEG support
                        await LoadStreamInWebView(url);

                        _isConnected = true;
                        ConnectButton.IsEnabled = false;
                        DisconnectButton.IsEnabled = true;
                        StatusLabel.Text = $"✅ Connected successfully! Streaming from: {url}";
                    }
                    else
                    {
                        StatusLabel.Text = $"❌ Camera returned HTTP {response.StatusCode}. Try 'Test in Browser' button.";

                        // Still try to load in WebView
                        await LoadStreamInWebView(url);
                        _isConnected = true;
                        ConnectButton.IsEnabled = false;
                        DisconnectButton.IsEnabled = true;
                    }
                }
                catch (TaskCanceledException)
                {
                    StatusLabel.Text = "❌ TIMEOUT: Camera not responding. Possible issues:\n" +
                                     "• Camera is offline\n" +
                                     "• Wrong IP address or port\n" +
                                     "• Firewall blocking connection\n" +
                                     "• Camera requires authentication";

                    // Still show the WebView with error message
                    ShowConnectionError(url, "Connection timeout - camera not responding");
                    ConnectButton.IsEnabled = true;
                }
                catch (HttpRequestException ex)
                {
                    var errorMsg = ex.InnerException?.Message ?? ex.Message;
                    StatusLabel.Text = $"❌ CONNECTION FAILED: {errorMsg}";

                    ShowConnectionError(url, $"Network error: {errorMsg}");
                    ConnectButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"❌ ERROR: {ex.Message}";
                ShowConnectionError(url, ex.Message);
                ConnectButton.IsEnabled = true;
            }
        }

        private void ShowConnectionError(string url, string errorMessage)
        {
            try
            {
                VideoWebView.IsVisible = true;
                VideoImage.IsVisible = false;

                var htmlContent = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <style>
                        body {{ margin: 0; padding: 20px; background: #1a1a1a; color: white; font-family: Arial, sans-serif; }}
                        .error-container {{ max-width: 800px; margin: 0 auto; }}
                        h1 {{ color: #f44336; }}
                        .error-box {{ background: #2d2d2d; border-left: 4px solid #f44336; padding: 20px; margin: 20px 0; border-radius: 5px; }}
                        .url-box {{ background: #333; padding: 15px; border-radius: 5px; margin: 15px 0; word-break: break-all; font-family: monospace; font-size: 14px; }}
                        .solutions {{ background: #2d2d2d; padding: 20px; border-radius: 5px; margin: 20px 0; }}
                        .solution {{ margin: 15px 0; padding: 15px; background: #1a1a1a; border-left: 3px solid #2196F3; border-radius: 3px; }}
                        .solution-title {{ color: #2196F3; font-weight: bold; margin-bottom: 10px; }}
                        .test-button {{ background: #2196F3; color: white; border: none; padding: 12px 24px; border-radius: 5px; cursor: pointer; font-size: 16px; margin: 10px 5px; }}
                        .test-button:hover {{ background: #1976D2; }}
                    </style>
                </head>
                <body>
                    <div class='error-container'>
                        <h1>⚠️ Connection Failed</h1>
                        
                        <div class='error-box'>
                            <strong>Error:</strong> {errorMessage}
                        </div>
                        
                        <div class='url-box'>
                            <strong>Target URL:</strong><br>{url}
                        </div>
                        
                        <div class='solutions'>
                            <h2>🔧 Troubleshooting Steps:</h2>
                            
                            <div class='solution'>
                                <div class='solution-title'>1. Verify Camera IP and Port</div>
                                • Check if 192.82.150.11 is the correct IP address<br>
                                • Port 8083 might be blocked or incorrect<br>
                                • Try different URLs using the 'Traffic Cam' preset button
                            </div>
                            
                            <div class='solution'>
                                <div class='solution-title'>2. Test Network Access</div>
                                • Ping 192.82.150.11 from command prompt<br>
                                • Try accessing from your web browser directly<br>
                                • Check if you need to be on a specific network/VPN
                            </div>
                            
                            <div class='solution'>
                                <div class='solution-title'>3. Check Firewall Settings</div>
                                • Windows Firewall might be blocking the connection<br>
                                • Antivirus software may be interfering<br>
                                • Router firewall settings
                            </div>
                            
                            <div class='solution'>
                                <div class='solution-title'>4. Try Alternative Access Methods</div>
                                • Click 'Test in Browser' button above<br>
                                • Use VLC Media Player (Click 'Download VLC')<br>
                                • Try accessing from another device on same network
                            </div>
                            
                            <div class='solution'>
                                <div class='solution-title'>5. Camera May Require Authentication</div>
                                • Some cameras need username/password<br>
                                • Format: http://username:password@192.82.150.11:8083/mjpg/video.mjpg<br>
                                • Contact camera administrator for credentials
                            </div>
                        </div>
                        
                        <div style='text-align: center; margin-top: 30px;'>
                            <button class='test-button' onclick='testBrowser()'>Open in Browser</button>
                            <button class='test-button' onclick='copyUrl()'>Copy URL</button>
                        </div>
                    </div>
                    
                    <script>
                        function testBrowser() {{
                            window.open('{url}', '_blank');
                        }}
                        
                        function copyUrl() {{
                            navigator.clipboard.writeText('{url}').then(function() {{
                                alert('URL copied to clipboard!');
                            }});
                        }}
                    </script>
                </body>
                </html>";

                VideoWebView.Source = new HtmlWebViewSource { Html = htmlContent };
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error showing diagnostics: {ex.Message}";
            }
        }

        private Task LoadStreamInWebView(string url)
        {
            try
            {
                VideoWebView.IsVisible = true;
                VideoImage.IsVisible = false;

                var htmlContent = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <meta http-equiv='Cache-Control' content='no-cache, no-store, must-revalidate'>
                    <meta http-equiv='Pragma' content='no-cache'>
                    <meta http-equiv='Expires' content='0'>
                    <style>
                        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
                        body {{ 
                            background: #000; 
                            display: flex; 
                            justify-content: center; 
                            align-items: center; 
                            height: 100vh; 
                            overflow: hidden;
                            font-family: Arial, sans-serif;
                        }}
                        #container {{
                            position: relative;
                            width: 100%;
                            height: 100%;
                            display: flex;
                            justify-content: center;
                            align-items: center;
                        }}
                        img {{ 
                            max-width: 100%; 
                            max-height: 100vh; 
                            object-fit: contain;
                            display: block;
                        }}
                        .info {{ 
                            position: absolute; 
                            top: 10px; 
                            left: 10px; 
                            color: white; 
                            background: rgba(0,0,0,0.8); 
                            padding: 10px; 
                            border-radius: 5px; 
                            font-size: 12px;
                            z-index: 100;
                            max-width: 300px;
                            word-break: break-word;
                        }}
                        .status {{ 
                            position: absolute; 
                            bottom: 10px; 
                            left: 10px; 
                            right: 10px;
                            color: white; 
                            background: rgba(0,0,0,0.8); 
                            padding: 10px; 
                            border-radius: 5px; 
                            font-size: 14px;
                            text-align: center;
                            z-index: 100;
                        }}
                        .loading {{ color: #ffc107; }}
                        .error {{ color: #f44336; }}
                        .success {{ color: #4caf50; }}
                        .spinner {{
                            border: 3px solid rgba(255,255,255,0.3);
                            border-top: 3px solid white;
                            border-radius: 50%;
                            width: 40px;
                            height: 40px;
                            animation: spin 1s linear infinite;
                            position: absolute;
                            top: 50%;
                            left: 50%;
                            transform: translate(-50%, -50%);
                        }}
                        @keyframes spin {{
                            0% {{ transform: translate(-50%, -50%) rotate(0deg); }}
                            100% {{ transform: translate(-50%, -50%) rotate(360deg); }}
                        }}
                    </style>
                </head>
                <body>
                    <div id='container'>
                        <div class='info' id='info'>
                            <strong>Camera Stream</strong><br>
                            URL: {url}
                        </div>
                        
                        <div class='spinner' id='spinner'></div>
                        
                        <img id='stream' style='display:none;' />
                        
                        <div class='status' id='status'>
                            <span class='loading'>⏳ Loading stream...</span>
                        </div>
                    </div>
                    
                    <script>
                        var streamImg = document.getElementById('stream');
                        var status = document.getElementById('status');
                        var spinner = document.getElementById('spinner');
                        var loadTimeout;
                        var errorCount = 0;
                        var maxErrors = 3;
                        
                        function updateStatus(message, className) {{
                            status.innerHTML = '<span class=""' + className + '"">' + message + '</span>';
                        }}
                        
                        function hideSpinner() {{
                            spinner.style.display = 'none';
                        }}
                        
                        function showStream() {{
                            streamImg.style.display = 'block';
                        }}
                        
                        function loadStream() {{
                            var timestamp = new Date().getTime();
                            var streamUrl = '{url}';
                            
                            // Add cache-busting parameter
                            if (streamUrl.indexOf('?') > -1) {{
                                streamUrl += '&_=' + timestamp;
                            }} else {{
                                streamUrl += '?_=' + timestamp;
                            }}
                            
                            streamImg.src = streamUrl;
                            
                            // Set timeout
                            clearTimeout(loadTimeout);
                            loadTimeout = setTimeout(function() {{
                                if (!streamImg.complete || streamImg.naturalHeight === 0) {{
                                    errorCount++;
                                    if (errorCount < maxErrors) {{
                                        updateStatus('⏳ Retrying connection... (Attempt ' + (errorCount + 1) + '/' + maxErrors + ')', 'loading');
                                        setTimeout(loadStream, 2000);
                                    }} else {{
                                        hideSpinner();
                                        updateStatus('❌ Failed to load stream after ' + maxErrors + ' attempts. Check camera URL or use Test in Browser.', 'error');
                                    }}
                                }}
                            }}, 15000);
                        }}
                        
                        streamImg.onload = function() {{
                            clearTimeout(loadTimeout);
                            errorCount = 0;
                            hideSpinner();
                            showStream();
                            updateStatus('✅ Stream loaded successfully! Displaying live video.', 'success');
                            
                            // Hide success message after 3 seconds
                            setTimeout(function() {{
                                status.style.display = 'none';
                            }}, 3000);
                        }};
                        
                        streamImg.onerror = function() {{
                            clearTimeout(loadTimeout);
                            errorCount++;
                            
                            if (errorCount < maxErrors) {{
                                updateStatus('⚠️ Connection lost. Retrying... (Attempt ' + (errorCount + 1) + '/' + maxErrors + ')', 'loading');
                                setTimeout(loadStream, 2000);
                            }} else {{
                                hideSpinner();
                                updateStatus('❌ Stream failed to load. Possible issues:<br>• Camera is offline<br>• Wrong URL<br>• Network firewall blocking connection<br><br>Try clicking ""Test in Browser"" button.', 'error');
                            }}
                        }};
                        
                        // Start loading
                        loadStream();
                    </script>
                </body>
                </html>";

                VideoWebView.Source = new HtmlWebViewSource { Html = htmlContent };
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error loading stream: {ex.Message}";
                ConnectButton.IsEnabled = true;
                return Task.CompletedTask;
            }
        }

        private async void OnPresetClicked(object sender, EventArgs e)
        {
            var cameraUrls = new[]
            {
                // WORKING PUBLIC TEST CAMERAS (verified)
                "http://webcam.mchcares.com/axis-cgi/mjpg/video.cgi",     // Hospital webcam
                "http://iris.not.iac.es/axis-cgi/mjpg/video.cgi",         // Canary Islands telescope
                "http://penobscot.MaineRoads.com/Streams/MDOT--3.stream/playlist.m3u8", // Maine DOT
                
                // Ontario Traffic Camera attempts (likely not accessible)
                "http://192.82.150.11/mjpg/video.mjpg",
                "http://192.82.150.11:80/video.mjpg",
                "http://192.82.150.11:8080/mjpg/video.mjpg",
                "http://192.82.150.11:8081/mjpg/video.mjpg",
                "http://192.82.150.11:8083/mjpg/video.mjpg",
            };

            var currentText = IpAddressEntry.Text;
            var currentIndex = Array.IndexOf(cameraUrls, currentText);
            var nextIndex = (currentIndex + 1) % cameraUrls.Length;

            IpAddressEntry.Text = cameraUrls[nextIndex];

            if (nextIndex < 3)
            {
                StatusLabel.Text = $"🧪 PUBLIC TEST CAMERA {nextIndex + 1}/3 - Click Connect to verify app works!";
            }
            else
            {
                StatusLabel.Text = $"Ontario Camera Attempt {nextIndex - 2}/5 - Testing...";

                // Quick connectivity test
                try
                {
                    PresetButton.IsEnabled = false;

                    using var quickTest = new HttpClient();
                    quickTest.Timeout = TimeSpan.FromSeconds(3);
                    var testResult = await quickTest.GetAsync(cameraUrls[nextIndex], HttpCompletionOption.ResponseHeadersRead);

                    StatusLabel.Text = $"✅ SUCCESS! This URL responds! Click Connect!";
                }
                catch (TaskCanceledException)
                {
                    StatusLabel.Text = $"⏱️ Timeout - Camera not responding. Try next preset.";
                }
                catch (HttpRequestException ex)
                {
                    var msg = ex.InnerException?.Message ?? ex.Message;
                    if (msg.Contains("refused"))
                        StatusLabel.Text = $"❌ Connection refused (port closed). Try next preset.";
                    else
                        StatusLabel.Text = $"⚠️ Error: {msg.Substring(0, Math.Min(50, msg.Length))}";
                }
                catch
                {
                    StatusLabel.Text = $"⚠️ Failed. Try next preset or use test cameras.";
                }
                finally
                {
                    PresetButton.IsEnabled = true;
                }
            }
        }

        private void OnOpenInVlcClicked(object sender, EventArgs e)
        {
            try
            {
                var url = IpAddressEntry.Text.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    StatusLabel.Text = "No URL to open in VLC";
                    return;
                }

                var vlcPath = FindVlcExecutable();
                if (vlcPath != null)
                {
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = vlcPath,
                            Arguments = $"\"{url}\"",
                            UseShellExecute = true
                        }
                    };
                    process.Start();
                    StatusLabel.Text = "Opening VLC with stream...";
                }
                else
                {
                    StatusLabel.Text = "VLC not found. Click 'Download VLC' to install it.";
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error opening VLC: {ex.Message}";
            }
        }

        private void OnDownloadVlcClicked(object sender, EventArgs e)
        {
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://www.videolan.org/vlc/",
                        UseShellExecute = true
                    }
                };
                process.Start();
                StatusLabel.Text = "Opening VLC download page...";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error opening download page: {ex.Message}";
            }
        }

        private async void OnTestInBrowserClicked(object sender, EventArgs e)
        {
            try
            {
                var url = IpAddressEntry.Text.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    StatusLabel.Text = "No URL to test in browser";
                    return;
                }

                StatusLabel.Text = "Testing camera URL...";

                try
                {
                    using var testClient = new HttpClient();
                    testClient.Timeout = TimeSpan.FromSeconds(5);
                    var response = await testClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                    StatusLabel.Text = $"Status: {response.StatusCode}. Opening in browser...";
                }
                catch (Exception ex)
                {
                    StatusLabel.Text = $"Test result: {ex.Message}. Opening anyway...";
                }

                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    }
                };
                process.Start();
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
            }
        }

        private string? FindVlcExecutable()
        {
            var possiblePaths = new[]
            {
                @"C:\Program Files\VideoLAN\VLC\vlc.exe",
                @"C:\Program Files (x86)\VideoLAN\VLC\vlc.exe",
                @"C:\Program Files\VLC\vlc.exe",
                @"C:\Program Files (x86)\VLC\vlc.exe"
            };

            foreach (var path in possiblePaths)
            {
                if (System.IO.File.Exists(path))
                    return path;
            }

            return null;
        }

        private void OnDisconnectClicked(object sender, EventArgs e)
        {
            Disconnect();
        }

        private void Disconnect()
        {
            _cancellationTokenSource?.Cancel();
            _isConnected = false;

            VideoWebView.IsVisible = false;
            VideoImage.IsVisible = false;
            NoVideoLabel.IsVisible = true;

            ConnectButton.IsEnabled = true;
            DisconnectButton.IsEnabled = false;
            VlcButtonGrid.IsVisible = false;

            StatusLabel.Text = "Disconnected";
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            Disconnect();
        }
    }
}