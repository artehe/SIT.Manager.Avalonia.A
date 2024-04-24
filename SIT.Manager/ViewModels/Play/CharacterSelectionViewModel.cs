﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SIT.Manager.Interfaces;
using SIT.Manager.Models.Aki;
using SIT.Manager.Models.Play;
using SIT.Manager.Services;
using SIT.Manager.Views.Play;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace SIT.Manager.ViewModels.Play;

public partial class CharacterSelectionViewModel : ObservableRecipient
{
    private readonly ILogger _logger;
    private readonly IAkiServerRequestingService _serverService;
    private readonly ILocalizationService _localizationService;
    private readonly IManagerConfigService _configService;
    private readonly IServiceProvider _serviceProvider;

    private AkiServer _connectedServer;

    public ObservableCollection<CharacterSummaryViewModel> SavedCharacterList { get; } = [];
    public ObservableCollection<CharacterSummaryViewModel> CharacterList { get; } = [];

    public IAsyncRelayCommand CreateCharacterCommand { get; }

    public CharacterSelectionViewModel(IServiceProvider serviceProvider,
        ILocalizationService localizationService,
        ILogger<CharacterSelectionViewModel> logger,
        IAkiServerRequestingService serverService,
        IManagerConfigService configService)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _localizationService = localizationService;
        _serverService = serverService;
        _configService = configService;

        try
        {
            _connectedServer = WeakReferenceMessenger.Default.Send<ConnectedServerRequestMessage>();
        }
        catch
        {
            _connectedServer = new AkiServer(new Uri("http://127.0.0.1:6969"))
            {
                Characters = [],
                Name = "N/A",
                Ping = -1
            };
        }

        CreateCharacterCommand = new AsyncRelayCommand(CreateCharacter);
    }

    [RelayCommand]
    private void Back()
    {
        WeakReferenceMessenger.Default.Send(new ServerDisconnectMessage(_connectedServer));
    }

    private async Task CreateCharacter()
    {
        (ContentDialogResult result, string username, string password, bool saveLoginDetails) = await new CreateCharacterDialogView().ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        AkiCharacter character = new(_connectedServer, username, password);

        _logger.LogInformation("Registering new character...");
        (string _, AkiLoginStatus status) = await _serverService.RegisterCharacterAsync(character);
        if (status != AkiLoginStatus.Success)
        {
            await new ContentDialog()
            {
                Title = _localizationService.TranslateSource("DirectConnectViewModelLoginErrorTitle"),
                Content = _localizationService.TranslateSource("DirectConnectViewModelLoginErrorDescription"),
                CloseButtonText = _localizationService.TranslateSource("DirectConnectViewModelButtonOk")
            }.ShowAsync();
            _logger.LogDebug("Register character failed with {status}", status);
            return;
        }

        if (saveLoginDetails)
        {
            character.ParentServer.Characters.Add(character);
            int index = _configService.Config.BookmarkedServers.FindIndex(x => x.Address == character.ParentServer.Address);
            if (index != -1 && !_configService.Config.BookmarkedServers[index].Characters.Any(x => x.Username == character.Username))
            {
                _configService.Config.BookmarkedServers[index].Characters.Add(character);
            }
            _configService.UpdateConfig(_configService.Config);
        }

        await ReloadCharacterList();
    }

    private async Task ReloadCharacterList()
    {
        CharacterList.Clear();
        List<AkiMiniProfile> miniProfiles = await _serverService.GetMiniProfilesAsync(_connectedServer);
        foreach (AkiMiniProfile profile in miniProfiles)
        {
            CharacterSummaryViewModel characterSummaryViewModel = ActivatorUtilities.CreateInstance<CharacterSummaryViewModel>(_serviceProvider, _connectedServer, profile);
            if (_connectedServer.Characters.Any(x => x.Username == profile.Username) == true)
            {
                SavedCharacterList.Add(characterSummaryViewModel);
            }
            else
            {
                CharacterList.Add(characterSummaryViewModel);
            }
        }
        _logger.LogDebug("{profileCount} mini profiles retrieved from {name}", miniProfiles.Count, _connectedServer.Name);
    }

    protected override async void OnActivated()
    {
        base.OnActivated();

        AkiServer? currentServer = _configService.Config.BookmarkedServers.FirstOrDefault(x => x.Address == _connectedServer.Address);
        if (currentServer != null)
        {
            _connectedServer = currentServer;
        }

        await ReloadCharacterList();
    }
}
