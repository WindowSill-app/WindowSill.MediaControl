using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.Graphics.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

using WindowSill.API;

namespace WindowSill.MediaControl;

internal sealed partial class MediaControlViewModel : ObservableObject
{
    private const uint HorizontalThumbnailSize = 40;
    private const uint VerticalThumbnailSize = 64;

    private readonly ILogger _logger;
    private readonly ISettingsProvider _settingsProvider;

    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;

    public MediaControlViewModel(ISettingsProvider settingsProvider)
    {
        _logger = this.Log();
        _settingsProvider = settingsProvider;
        _settingsProvider.SettingChanged += SettingsProvider_SettingChanged;
        ShowTitleArtistAndThumbnail = _settingsProvider.GetSetting(Settings.Settings.ShowTitleArtistAndThumbnail);

        InitializeAsync().Forget();
    }

    [ObservableProperty]
    public partial string? SongName { get; set; }

    [ObservableProperty]
    public partial string? ArtistName { get; set; }

    [ObservableProperty]
    public partial string? SongAndArtistName { get; set; }

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    [ObservableProperty]
    public partial ImageSource? ThumbnailLarge { get; set; }

    [ObservableProperty]
    public partial bool IsNextAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsPreviousAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsPlayPauseAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial bool ShowTitleArtistAndThumbnail { get; set; }

    [ObservableProperty]
    public partial bool ShouldAppearInSill { get; set; }

    [RelayCommand]
    private async Task PlayPauseAsync()
    {
        try
        {
            GlobalSystemMediaTransportControlsSession? currentSession = _currentSession;
            if (currentSession is not null)
            {
                GlobalSystemMediaTransportControlsSessionPlaybackInfo? playback = currentSession.GetPlaybackInfo();
                if (playback is not null)
                {
                    if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        await currentSession.TryPauseAsync();
                    }
                    else if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                    {
                        await currentSession.TryPlayAsync();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while trying to play or pause media.");
        }
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        try
        {
            GlobalSystemMediaTransportControlsSession? currentSession = _currentSession;
            if (currentSession is not null)
            {
                await currentSession.TrySkipNextAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while trying to play next media.");
        }
    }

    [RelayCommand]
    private async Task PreviousAsync()
    {
        try
        {
            GlobalSystemMediaTransportControlsSession? currentSession = _currentSession;
            if (currentSession is not null)
            {
                await currentSession.TrySkipPreviousAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while trying to play previous media.");
        }
    }

    private void SettingsProvider_SettingChanged(ISettingsProvider sender, SettingChangedEventArgs args)
    {
        if (args.SettingName == Settings.Settings.ShowTitleArtistAndThumbnail.Name)
        {
            ShowTitleArtistAndThumbnail = _settingsProvider.GetSetting(Settings.Settings.ShowTitleArtistAndThumbnail);
        }
        else if (args.SettingName == PredefinedSettings.SillLocation.Name)
        {
            UpdateInfoAsync(_sessionManager?.GetCurrentSession()).Forget();
        }
    }

    private void SessionManager_SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        UpdateInfoAsync(_sessionManager?.GetCurrentSession()).Forget();
    }

    private void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        UpdateInfoAsync(_sessionManager?.GetCurrentSession()).Forget();
    }

    private void CurrentSession_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        UpdateInfoAsync(sender).Forget();
    }

    private void CurrentSession_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        UpdateInfoAsync(sender).Forget();
    }

    private async Task InitializeAsync()
    {
        await ThreadHelper.RunOnUIThreadAsync(async () =>
        {
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

            if (_sessionManager is not null)
            {
                _sessionManager.SessionsChanged += SessionManager_SessionsChanged;
                _sessionManager.CurrentSessionChanged += SessionManager_CurrentSessionChanged;

                await UpdateInfoAsync(_sessionManager?.GetCurrentSession());
            }
        });
    }

    private async Task UpdateInfoAsync(GlobalSystemMediaTransportControlsSession? session)
    {
        await ThreadHelper.RunOnUIThreadAsync(async () =>
        {
            if (_currentSession is not null)
            {
                _currentSession.PlaybackInfoChanged -= CurrentSession_PlaybackInfoChanged;
                _currentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
            }

            _currentSession = session;

            if (_currentSession is not null)
            {
                _currentSession.PlaybackInfoChanged += CurrentSession_PlaybackInfoChanged;
                _currentSession.MediaPropertiesChanged += CurrentSession_MediaPropertiesChanged;

                try
                {
                    GlobalSystemMediaTransportControlsSessionMediaProperties mediaInfo = await _currentSession.TryGetMediaPropertiesAsync();
                    GlobalSystemMediaTransportControlsSessionPlaybackInfo playback = _currentSession.GetPlaybackInfo();

                    SongName = mediaInfo.Title;
                    ArtistName = mediaInfo.Artist;
                    SongAndArtistName = $"{mediaInfo.Artist} - {mediaInfo.Title}";

                    (ImageSource? thumbnail, ImageSource? thumbnailLarge) = await GetThumbnailImageSourceAsync(mediaInfo.Thumbnail);
                    Thumbnail = thumbnail;
                    ThumbnailLarge = thumbnailLarge;

                    UpdatePlaybackInfo(playback);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while trying to retrieve media information.");
                }
            }

            // Ensure proper state reset when session is null
            UpdatePlaybackInfo(null);
        });
    }

    private void UpdatePlaybackInfo(GlobalSystemMediaTransportControlsSessionPlaybackInfo? playback)
    {
        if (playback is not null)
        {
            IsNextAvailable = playback.Controls.IsNextEnabled;
            IsPreviousAvailable = playback.Controls.IsPreviousEnabled;
            IsPlayPauseAvailable = playback.Controls.IsPauseEnabled || playback.Controls.IsPlayEnabled;
            IsPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            ShouldAppearInSill = !string.IsNullOrEmpty(SongName);
        }
        else
        {
            ShouldAppearInSill = false;
            IsNextAvailable = false;
            IsPreviousAvailable = false;
            IsPlayPauseAvailable = false;
            IsPlaying = false;
        }
    }

    private async Task<(ImageSource? thumbnail, ImageSource? thumbnailLarge)> GetThumbnailImageSourceAsync(IRandomAccessStreamReference thumbnail)
    {
        try
        {
            if (thumbnail is not null)
            {
                using IRandomAccessStreamWithContentType stream = await thumbnail.OpenReadAsync();
                if (stream != null)
                {
                    // Create the decoder from the stream
                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

                    // Get the SoftwareBitmap representation of the file
                    SoftwareBitmap thumbnailLarge = await decoder.GetSoftwareBitmapAsync();
                    if (thumbnailLarge.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || thumbnailLarge.BitmapAlphaMode == BitmapAlphaMode.Straight)
                    {
                        thumbnailLarge = SoftwareBitmap.Convert(thumbnailLarge, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    }

                    // Scale down for the small thumbnail
                    uint size
                        = _settingsProvider.GetSetting(PredefinedSettings.SillLocation)
                        is SillLocation.Left or SillLocation.Right
                        ? VerticalThumbnailSize
                        : HorizontalThumbnailSize;

                    var transform = new BitmapTransform()
                    {
                        ScaledWidth = size,
                        ScaledHeight = size,
                        InterpolationMode = BitmapInterpolationMode.Fant
                    };
                    SoftwareBitmap thumbnailSmall
                        = await decoder.GetSoftwareBitmapAsync(
                            thumbnailLarge.BitmapPixelFormat,
                            thumbnailLarge.BitmapAlphaMode,
                            transform,
                            ExifOrientationMode.IgnoreExifOrientation,
                            ColorManagementMode.ColorManageToSRgb);

                    var sourceLarge = new SoftwareBitmapSource();
                    await sourceLarge.SetBitmapAsync(thumbnailLarge);

                    var sourceSmall = new SoftwareBitmapSource();
                    await sourceSmall.SetBitmapAsync(thumbnailSmall);
                    return (sourceSmall, sourceLarge);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while trying to retirve media thumbnail.");
        }

        return (null, null);
    }
}
