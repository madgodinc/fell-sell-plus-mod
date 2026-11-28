using System;
using System.Collections.Generic;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FellSellPlus.Coop;

// The "Co-op" entry of the main menu and its window: host a game, join a Steam friend who hosts, send
// a Steam invitation, or go back to single player. The choice holds for this session of the game and
// wins over the cfg; the player then loads his save as usual.
//
// Nothing here waits for a Steam call result (the game's callback pump takes them, see SteamLobby):
// who hosts is read from the friends list every second, this player's own lobby from his own "game
// played" entry, and an invitation accepted while the game is closed arrives as the launch argument
// "+connect_lobby <id>".
//
// The same entry sits in the pause menu ("Invite a friend"): there the window turns the running game
// into a host with the first invitation, or joins a friend who hosts, without a trip to the main menu.
// While a friend hosts and this player is in no session, the entry itself reads "<friend> is playing:
// join" and one press joins (from the main menu it also loads the newest save).
//
// The same window opens in the village from the Horde mod's statue ("Invite a friend"): there it
// starts hosting at once, offers the invitations and leaves out what belongs to the main menu.
//
// The menu entry and every button of the window are copies of the game's own menu button, so font,
// hover frame and gamepad behaviour are the game's.
internal static class CoopMenu
{
    const int Rows = 6;
    const float W = 980f, H = 800f;

    sealed class Row { public GameObject Root; public TextMeshProUGUI Name, State, ActionLabel; public Button Action; public ulong Friend; public bool Hosting; }

    static GameObject _entry, _template, _root, _continue, _close, _hostButton;
    static Image _dim;
    static TextMeshProUGUI _entryLabel, _state, _empty, _hint, _title, _friendsTitle, _hostLabel, _soloLabel, _continueLabel, _closeLabel;
    static readonly List<Row> _rows = new();
    static readonly Dictionary<ulong, float> _invited = new();
    static CanvasGroup _menuGroup;
    static bool _open, _builtRu, _launchChecked, _inGame, _pause, _autoContinue;
    static Action _onClosed;
    static float _nextFind, _nextRefresh, _nextLabel, _nextFriends;
    // Invitations asked for before Steam has made the lobby (it takes a moment after hosting starts).
    static readonly Dictionary<ulong, float> _pendingInvites = new();
    static ulong _hostFriend;
    static string _hostFriendName = "";
    static int _openedFrame;
    static string _joinName = "";

    static readonly Color Ink = new(1f, 1f, 1f, 1f);
    static readonly Color Muted = new(0.80f, 0.78f, 0.72f, 1f);
    static readonly Color Gold = new(1f, 0.85f, 0.45f, 1f);

    static bool Ru => Progression.GameQuery.IsRussian();
    internal static bool IsOpen => _open;

    public static void Tick()
    {
        if (!CoopSettings.MenuButton.Value) return;
        string scene = SceneManager.GetActiveScene().name;
        float now = Time.unscaledTime;
        try
        {
            if (scene == "MainMenu" && !_launchChecked && SteamLobby.Ready())
            {
                _launchChecked = true;
                ulong lobby = SteamLobby.LaunchLobby();
                if (lobby != 0) { Plugin.L.LogInfo($"Co-op: started from a Steam invitation (lobby {lobby})."); JoinLobby(lobby); _autoContinue = true; }
            }
            if (_entry == null && now >= _nextFind) { _nextFind = now + 0.5f; AddEntry(); }
            if (now >= _nextFriends) { _nextFriends = now + 1.5f; FindHostingFriend(); SendPendingInvites(); }
            if (_entry != null && now >= _nextLabel) { _nextLabel = now + 0.5f; _entryLabel.text = EntryText(scene); }
            // An invitation accepted while the game was closed: the newest save loads by itself.
            if (_autoContinue && scene == "MainMenu" && _entry != null)
            {
                var menu = Menu();
                if (menu != null && !menu.IsLoading && !menu.IsLoadingScreenActive)
                {
                    _autoContinue = false;
                    if (menu.CanContinue()) { Plugin.L.LogInfo("Co-op: loading your newest save to join the invitation."); Close(); menu.Continue(); }
                    else Open(); // no save yet: the window says what to do
                }
            }
            if (!_open) return;
            if (_inGame)
            {
                // In the village the game's window manager closes the window on Esc by switching it off.
                if (scene != "MAIN" || _root == null || !_root.activeSelf) { Close(); return; }
            }
            else if (_pause)
            {
                // Esc closes the pause menu underneath (the game's own key): this window goes with it.
                var menu = Menu();
                if (_root == null || !_root.activeSelf || menu == null || menu.IsMainMenu || !menu.IsPauseMenuOpen) { Close(); return; }
            }
            else
            {
                if (scene != "MainMenu" || _root == null || !_root.activeSelf) { Close(); return; }
                var kb = Keyboard.current;
                if (Time.frameCount != _openedFrame && kb != null && kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            }
            if (now >= _nextRefresh) { _nextRefresh = now + 1f; Refresh(); }
        }
        catch (Exception e) { CoopModule.Warn("co-op menu", e); }
    }

    static MainPauseMenu Menu()
    {
        MainPauseMenu menu = null;
        try { menu = MainPauseMenu.Instance; } catch { }
        return menu != null ? menu : UnityEngine.Object.FindObjectOfType<MainPauseMenu>();
    }

    // A session starts in the main menu or in the village: a run in progress has its own enemies and floor.
    static bool CanStartHere(string scene) => scene == "MainMenu" || scene == "MAIN";
    static bool Hosting => CoopSettings.Session == "host" || (CoopModule.Running && CoopSettings.IsHost);
    static bool Guest => CoopSettings.Session == "client" || (CoopModule.Running && !CoopSettings.IsHost);

    static void FindHostingFriend()
    {
        _hostFriend = 0; _hostFriendName = "";
        if (!SteamLobby.Ready()) return;
        foreach (var f in SteamLobby.Friends())
            if (f.Lobby != 0) { _hostFriend = f.Id; _hostFriendName = f.Name; return; }
    }

    // The entry itself offers the join while a friend hosts and this player is in no session.
    static bool QuickJoin(string scene) => _hostFriend != 0 && !Hosting && !Guest && !CoopModule.Running && CanStartHere(scene);

    static void SendPendingInvites()
    {
        if (_pendingInvites.Count == 0) return;
        float now = Time.unscaledTime;
        foreach (var kv in new List<KeyValuePair<ulong, float>>(_pendingInvites))
        {
            if (SteamLobby.Invite(kv.Key)) { _pendingInvites.Remove(kv.Key); _invited[kv.Key] = now; }
            else if (now - kv.Value > 20f) { _pendingInvites.Remove(kv.Key); _invited[kv.Key] = -1f; }
        }
    }

    static string EntryText(string scene)
    {
        bool ru = Ru;
        if (CoopModule.Running && FirstPersonController.Instance != null)
        {
            if (CoopSettings.IsHost)
                return CoopModule.HasPartner ? (ru ? "Кооператив: друг в игре" : "Co-op: friend connected") : (ru ? "Кооператив: ждём друга" : "Co-op: waiting for a friend");
            string host = DungeonSync.HostConnected && DungeonSync.HostName.Length > 0 ? DungeonSync.HostName : _joinName;
            return (ru ? "Кооператив: у " : "Co-op: with ") + (host.Length > 0 ? host : ru ? "друга" : "a friend");
        }
        if (QuickJoin(scene)) return ru ? $"{_hostFriendName} играет: присоединиться" : $"{_hostFriendName} is playing: join";
        return CoopSettings.Session switch
        {
            "host" => ru ? "Кооператив: вы хост" : "Co-op: hosting",
            "client" => (ru ? "Кооператив: к " : "Co-op: joining ") + (_joinName.Length > 0 ? _joinName : ru ? "другу" : "a friend"),
            _ => scene == "MainMenu" ? (ru ? "Кооператив" : "Co-op") : (ru ? "Позвать друга" : "Invite a friend"),
        };
    }

    static void EntryPressed()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (QuickJoin(scene)) { JoinAndGo(_hostFriend, _hostFriendName); return; }
        _pause = scene != "MainMenu";
        Open();
        if (!_open) _pause = false;
    }

    // Join a friend and get out of the way: from the main menu the newest save loads, from the pause
    // menu the game goes on and the link comes up while the player stands in his village.
    static void JoinAndGo(ulong friend, string name)
    {
        bool inMenu = SceneManager.GetActiveScene().name == "MainMenu";
        var menu = Menu();
        Join(friend, name);
        if (inMenu)
        {
            if (menu != null && menu.CanContinue()) { Close(); menu.Continue(); }
            else if (!_open) { _pause = false; Open(); }
            return;
        }
        Close();
        try { var wm = WindowsManager.Instance; if (wm != null && menu != null && menu.IsPauseMenuOpen) wm.HandlePauseInput(); }
        catch (Exception e) { CoopModule.Warn("closing the pause menu", e); }
    }

    // ---------------------------------------------------------------- the entry in the main menu

    static void AddEntry()
    {
        var menu = Menu();
        if (menu == null) return;
        Transform load = null;
        foreach (var t in menu.GetComponentsInChildren<Transform>(true)) if (t.name == "LoadBTN") { load = t; break; }
        if (load == null || load.GetComponent<Button>() == null) { _nextFind = Time.unscaledTime + 5f; Plugin.L.LogWarning("Co-op: the menu has no Load button to copy, no Co-op entry."); return; }

        // In neither of the menu's "only in ..." lists: the entry shows in the main menu and in the pause menu.
        _entry = Copy(load.gameObject, load.parent, "FSP_CoopBTN", new Action(EntryPressed), out _entryLabel);
        _entry.transform.SetSiblingIndex(load.GetSiblingIndex() + 1);
        _entryLabel.text = EntryText(SceneManager.GetActiveScene().name);
        try { menu.RebuildMenuNavigation(); } catch { }

        if (_template == null)
        {
            _template = Copy(load.gameObject, null, "FSP_CoopButtonTemplate", null, out _);
            _template.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_template);
            foreach (var le in _template.GetComponents<LayoutElement>()) UnityEngine.Object.DestroyImmediate(le);
        }
        _menuGroup = load.parent.GetComponent<CanvasGroup>();
        if (_menuGroup == null) _menuGroup = load.parent.gameObject.AddComponent<CanvasGroup>();
        Plugin.L.LogInfo($"Co-op: menu entry added ({(menu.IsMainMenu ? "main menu" : "pause menu")}).");
    }

    // A copy of a game menu button that does what we say: the scene's own click target and its
    // localised label are taken off, the look stays.
    static GameObject Copy(GameObject source, Transform parent, string name, Action onClick, out TextMeshProUGUI label)
    {
        var go = parent != null ? UnityEngine.Object.Instantiate(source, parent) : UnityEngine.Object.Instantiate(source);
        go.name = name;
        foreach (var c in go.GetComponentsInChildren<Component>(true))
        {
            if (c == null) continue;
            if (c.GetIl2CppType().Name.Contains("Localize")) UnityEngine.Object.DestroyImmediate(c);
        }
        var b = go.GetComponent<Button>();
        b.onClick = new Button.ButtonClickedEvent();
        if (onClick != null) b.onClick.AddListener((UnityAction)onClick);
        label = go.GetComponentInChildren<TextMeshProUGUI>(true);
        go.SetActive(true);
        return go;
    }

    // ---------------------------------------------------------------- choices

    static void Host()
    {
        CoopModule.Stop();
        CoopSettings.Session = "host"; _joinName = "";
        SteamLobby.WantFriend = 0; SteamLobby.WantLobby = 0;
        SteamLobby.OpenLobby();
        Plugin.L.LogInfo("Co-op menu: hosting this session.");
        Refresh();
    }

    static void Solo()
    {
        CoopModule.Stop();
        CoopSettings.Session = "off"; _joinName = "";
        SteamLobby.WantFriend = 0; SteamLobby.WantLobby = 0;
        SteamLobby.CloseLobby();
        Plugin.L.LogInfo("Co-op menu: single player this session.");
        Refresh();
    }

    static void Join(ulong friend, string name)
    {
        CoopModule.Stop();
        SteamLobby.CloseLobby();
        CoopSettings.Session = "client"; _joinName = name;
        SteamLobby.WantFriend = friend; SteamLobby.WantLobby = 0;
        Plugin.L.LogInfo($"Co-op menu: joining {name} this session.");
        Refresh();
    }

    static void JoinLobby(ulong lobby)
    {
        CoopModule.Stop();
        SteamLobby.CloseLobby();
        CoopSettings.Session = "client"; _joinName = "";
        SteamLobby.WantFriend = 0; SteamLobby.WantLobby = lobby;
    }

    // The first invitation from the pause menu makes this game the host; Steam needs a moment to make
    // the lobby, so the invitation waits for it (SendPendingInvites).
    static void Invite(ulong friend)
    {
        if (!Hosting)
        {
            if (!CanStartHere(SceneManager.GetActiveScene().name)) return;
            Host();
        }
        if (SteamLobby.Invite(friend)) _invited[friend] = Time.unscaledTime;
        else { _invited.Remove(friend); _pendingInvites[friend] = Time.unscaledTime; }
        Refresh();
    }

    static void RowPressed(int i)
    {
        var row = _rows[i];
        if (row.Friend == 0) return;
        if (row.Hosting) JoinAndGo(row.Friend, row.Name.text); else Invite(row.Friend);
    }

    static void ContinueGame()
    {
        var menu = Menu();
        Close();
        if (menu != null && menu.CanContinue()) menu.Continue();
    }

    // ---------------------------------------------------------------- window

    // From the village (the Horde statue): host unless this player is somebody's guest, and show
    // the friends to invite. onClosed runs when the window is gone.
    internal static bool OpenInGame(Action onClosed)
    {
        if (_open) return true;
        if (_template == null) return false; // the main menu entry is switched off (MenuButton)
        _inGame = true; _pause = false;
        Open();
        if (!_open) { _inGame = false; return false; }
        _onClosed = onClosed;
        var wm = WindowsManager.Instance;
        try { if (wm != null && !wm.IsWindowInStack(_root)) { wm.RegisterWindow(_root); wm.OpenWindow(_root); } }
        catch (Exception e) { CoopModule.Warn("co-op window", e); }
        bool guest = CoopModule.Running && !CoopSettings.IsHost;
        bool lobbyUp = CoopModule.Running && CoopSettings.IsHost && SteamLobby.Active;
        // A host already playing with a partner over a direct link keeps that session.
        if (!guest && !lobbyUp && !CoopModule.HasPartner) Host();
        return true;
    }

    internal static void Open()
    {
        if (_open) return;
        if (_root != null && _builtRu != Ru) { UnityEngine.Object.Destroy(_root); _root = null; }
        if (_root == null) { Build(); _builtRu = Ru; }
        if (_root == null) return;
        _open = true; _openedFrame = Time.frameCount;
        _root.SetActive(true);
        // The menu underneath steps aside: it must not show through, nor take Enter or a gamepad press
        // meant for this window.
        if (_menuGroup != null && !_inGame) { _menuGroup.alpha = 0f; _menuGroup.interactable = false; _menuGroup.blocksRaycasts = false; }
        Refresh();
        try { EventSystem.current?.SetSelectedGameObject(_closeLabel.transform.parent.gameObject); } catch { }
    }

    internal static void Close()
    {
        if (!_open) return;
        _open = false;
        if (_inGame)
        {
            _inGame = false;
            var wm = WindowsManager.Instance;
            try { if (wm != null && _root != null) { wm.CloseWindow(_root); wm.UnregisterWindow(_root); } }
            catch (Exception e) { CoopModule.Warn("co-op window", e); }
            if (_root != null) _root.SetActive(false);
            var done = _onClosed; _onClosed = null;
            try { done?.Invoke(); } catch (Exception e) { CoopModule.Warn("co-op window", e); }
            return;
        }
        _pause = false;
        if (_root != null) _root.SetActive(false);
        if (_menuGroup != null) { _menuGroup.alpha = 1f; _menuGroup.interactable = true; _menuGroup.blocksRaycasts = true; }
        if (_entry != null) { _entryLabel.text = EntryText(SceneManager.GetActiveScene().name); try { EventSystem.current?.SetSelectedGameObject(_entry); } catch { } }
    }

    static void Refresh()
    {
        if (_root == null) return;
        bool ru = Ru;
        string scene = SceneManager.GetActiveScene().name;
        string session = CoopSettings.Session;
        bool live = _inGame || _pause;
        bool canStart = CanStartHere(scene);
        bool guest = live && CoopModule.Running && !CoopSettings.IsHost;
        bool hosting = session == "host" || (live && CoopModule.Running && CoopSettings.IsHost);
        string partner = "";
        foreach (var rem in CoopModule.Remotes) { partner = rem.Name; break; }
        _hint.text = _inGame
            ? (ru ? "Приглашение придёт другу в Steam. Когда друг войдёт в игру, примите вызов у статуи и войдите в подземелье: он пойдёт следом."
                  : "The invitation reaches your friend on Steam. Once your friend has joined, accept the challenge at the statue and enter the dungeon: he follows.")
            : _pause
            ? (!canStart && !CoopModule.Running
                ? (ru ? "Позвать друга или войти к нему можно из деревни: вернитесь из похода и откройте это окно снова."
                      : "Inviting a friend or joining one starts in the village: come back from the run and open this window again.")
                : (ru ? "Пригласите друга (он получит приглашение в Steam) или войдите к другу, который уже играет. Вместе вы ходите в подземелье и в лес, деревня у каждого своя."
                      : "Invite a friend (he gets a Steam invitation) or join a friend who is already playing. You share the dungeon and the forest; everyone keeps his own village."))
            : (ru ? "«Войти» у играющего друга само загрузит ваше сохранение. Чтобы позвать друга, загрузите игру и нажмите Esc: «Позвать друга»."
                  : "\"Join\" next to a friend who is playing loads your save by itself. To invite a friend, load your game and press Esc: \"Invite a friend\".");
        _state.text = guest ? (partner.Length > 0 ? (ru ? $"Вы в игре у {partner}." : $"You are in {partner}'s game.")
                                                  : (ru ? $"Подключаемся к {(_joinName.Length > 0 ? _joinName : "другу")}..." : $"Connecting to {(_joinName.Length > 0 ? _joinName : "your friend")}..."))
            : live && hosting ? (partner.Length > 0 ? (ru ? $"Вы хост. Друг подключился: {partner}." : $"You are the host. Friend connected: {partner}.")
                                                    : (ru ? "Вы хост. Ждём друга: пригласите его из списка ниже." : "You are the host. Waiting for a friend: invite one from the list below."))
            : session switch
        {
            "host" => ru ? "Вы хост. Друзья смогут войти, как только вы загрузите сохранение." : "You are the host. Friends can join once you have loaded your save.",
            "client" => ru ? $"Вы войдёте в игру {(_joinName.Length > 0 ? _joinName : "друга")}, как только загрузите сохранение." : $"You will join {(_joinName.Length > 0 ? _joinName + "'s" : "your friend's")} game once you have loaded your save.",
            "off" => ru ? "Одиночная игра." : "Single player.",
            _ => CoopSettings.Enabled.Value
                ? (ru ? $"По настройкам мода: {(CoopSettings.IsHost ? "хост" : "гость")}." : $"From the mod's settings: {(CoopSettings.IsHost ? "host" : "guest")}.")
                : (ru ? "Одиночная игра." : "Single player."),
        };
        _hostLabel.color = hosting ? Gold : Ink;
        _soloLabel.color = session == "off" || (session == null && !CoopSettings.Enabled.Value) ? Gold : Ink;
        _hostButton.SetActive(canStart || hosting);

        var friends = SteamLobby.Friends();
        float now = Time.unscaledTime;
        for (int i = 0; i < Rows; i++)
        {
            var row = _rows[i];
            if (i >= friends.Count) { row.Root.SetActive(false); row.Friend = 0; continue; }
            var f = friends[i];
            row.Friend = f.Id; row.Hosting = f.Lobby != 0;
            row.Name.text = f.Name;
            bool joiningHim = session == "client" && (SteamLobby.WantFriend == f.Id || (f.Lobby != 0 && SteamLobby.WantLobby == f.Lobby));
            _invited.TryGetValue(f.Id, out float invitedAt);
            string state;
            if (f.Lobby != 0) state = ru ? "ждёт игроков" : "is hosting";
            else if (f.InGame) state = ru ? "в игре" : "in the game";
            else state = ru ? "в сети" : "online";
            if (_pendingInvites.ContainsKey(f.Id)) state = ru ? "отправляем приглашение..." : "sending the invitation...";
            else if (invitedAt > 0f && now - invitedAt < 20f) state = ru ? "приглашение отправлено" : "invitation sent";
            else if (invitedAt < 0f) state = ru ? "лобби ещё создаётся, повторите" : "the lobby is not ready yet, try again";
            row.State.text = state;
            row.State.color = f.Lobby != 0 ? Gold : Muted;
            // Joining is offered for a friend who hosts (not from the statue's window, which only invites).
            // Inviting needs this player to host; from the pause menu the invitation itself starts hosting.
            bool canAct = f.Lobby != 0 ? !joiningHim && !_inGame && canStart
                : !guest && !_pendingInvites.ContainsKey(f.Id) && (hosting || (_pause && canStart && session != "client"));
            row.Action.gameObject.SetActive(canAct);
            row.ActionLabel.text = f.Lobby != 0 ? (ru ? "Войти" : "Join") : (ru ? "Пригласить" : "Invite");
            row.Root.SetActive(true);
        }
        _empty.gameObject.SetActive(friends.Count == 0);
        _empty.text = !SteamLobby.Ready() ? (ru ? "Steam недоступен." : "Steam is not available.") : (ru ? "Никого из друзей нет в сети." : "None of your friends is online.");
        var menu = Menu();
        _continue.SetActive(!live && menu != null && menu.CanContinue());
        // The village in daylight needs a darker veil than the menu's night scene; Close stands alone there.
        _dim.color = new Color(0.03f, 0.025f, 0.02f, live ? 0.92f : 0.74f);
        Place(_close.GetComponent<RectTransform>(), live ? 0 : 170, H - 44, 300, 60);
    }

    static void Build()
    {
        try
        {
            if (_template == null) return;
            _rows.Clear();
            bool ru = Ru;
            // The template outlives the main menu scene, the entry does not.
            var font = _template.GetComponentInChildren<TextMeshProUGUI>(true)?.font;

            _root = new GameObject("FellSellCoop_Menu");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            // The menu draws its entries straight over the scene; this window does the same over a
            // darkened scene, with one thin rule under the title instead of a boxed panel.
            var dim = Rect("Dim", _root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _dim = dim.gameObject.AddComponent<Image>();

            var panel = Rect("Panel", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H));

            _title = Text(panel, "Title", font, ru ? "Кооператив" : "Co-op", 54, Ink, TextAlignmentOptions.Center);
            Place(_title.rectTransform, 0, 50, W, 92);
            var rule = Rect("Rule", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -104), new Vector2(520, 2));
            rule.gameObject.AddComponent<Image>().color = new Color(1f, 0.85f, 0.45f, 0.55f);

            _state = Text(panel, "State", font, "", 26, Gold, TextAlignmentOptions.Center);
            Place(_state.rectTransform, 0, 146, W, 46);

            _hostButton = MakeButton(panel, ru ? "Создать игру" : "Host a game", -190, 216, 340, 64, 32, new Action(Host), out _hostLabel);
            MakeButton(panel, ru ? "Одиночная игра" : "Single player", 190, 216, 340, 64, 32, new Action(Solo), out _soloLabel);

            _friendsTitle = Text(panel, "FriendsTitle", font, ru ? "Друзья в Steam" : "Steam friends", 22, Muted, TextAlignmentOptions.Left);
            Place(_friendsTitle.rectTransform, -W / 2 + 60 + 200, 290, 400, 40);

            for (int i = 0; i < Rows; i++)
            {
                int idx = i;
                float y = 336 + i * 50;
                var rowRt = Rect("Friend" + i, panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -y), new Vector2(W - 120, 50));
                var row = new Row { Root = rowRt.gameObject };
                row.Name = Text(rowRt, "Name", font, "", 28, Ink, TextAlignmentOptions.Left);
                PlaceIn(row.Name.rectTransform, -250, 0, 360, 48);
                row.State = Text(rowRt, "State", font, "", 22, Muted, TextAlignmentOptions.Left);
                PlaceIn(row.State.rectTransform, 110, 0, 340, 40);
                var b = MakeButton(rowRt, "", 0, 0, 210, 48, 26, new Action(() => RowPressed(idx)), out row.ActionLabel);
                PlaceIn(b.GetComponent<RectTransform>(), 325, 0, 210, 48);
                row.Action = b.GetComponent<Button>();
                _rows.Add(row);
            }
            _empty = Text(panel, "Empty", font, "", 24, Muted, TextAlignmentOptions.Center);
            Place(_empty.rectTransform, 0, 356, W - 120, 44);

            _hint = Text(panel, "Hint", font, "", 21, Muted, TextAlignmentOptions.Center);
            _hint.enableWordWrapping = true;
            Place(_hint.rectTransform, 0, H - 126, W - 160, 70);

            _continue = MakeButton(panel, ru ? "Продолжить" : "Continue", -170, H - 44, 300, 60, 30, new Action(ContinueGame), out _continueLabel);
            _close = MakeButton(panel, ru ? "Закрыть" : "Close", 170, H - 44, 300, 60, 30, new Action(Close), out _closeLabel);
            _root.SetActive(false);
        }
        catch (Exception e)
        {
            Plugin.L.LogError("Co-op menu window failed: " + e);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
        }
    }

    static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    // x from the panel's centre, y down from its top edge.
    static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
    }

    static void PlaceIn(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h);
    }

    // With this game's font a box lower than about 1.6 of the font size draws nothing: Overflow, tall boxes.
    static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, string text, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>().SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text; t.color = color; t.alignment = align; t.fontSize = size;
        t.raycastTarget = false;
        t.enableAutoSizing = false;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    static GameObject MakeButton(Transform parent, string label, float x, float y, float w, float h, float size, Action onClick, out TextMeshProUGUI text)
    {
        var go = Copy(_template, parent, "Button", onClick, out text);
        var rt = go.GetComponent<RectTransform>();
        rt.localScale = Vector3.one;
        Place(rt, x, y, w, h);
        text.text = label;
        text.enableAutoSizing = false; text.fontSize = size;
        text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Overflow;
        return go;
    }

    // ---------------------------------------------------------------- dev

    internal static string Dev(string[] a)
    {
        string what = a.Length > 1 ? a[1] : "state";
        switch (what)
        {
            case "open": Open(); break;
            case "entry": EntryPressed(); break; // what a click on the menu entry does
            case "ingame": return "in-game window " + OpenInGame(null);
            case "close": Close(); break;
            case "host": Host(); break;
            case "solo": Solo(); break;
            case "continue": ContinueGame(); break;
            case "join":
            case "invite":
            {
                var friends = SteamLobby.Friends();
                int i = a.Length > 2 ? int.Parse(a[2]) : 0;
                if (i >= friends.Count) return "no such friend";
                if (what == "join") JoinAndGo(friends[i].Id, friends[i].Name);
                else { Invite(friends[i].Id); return "invite " + (_pendingInvites.ContainsKey(friends[i].Id) ? "waits for the lobby" : "sent") + " to " + friends[i].Name; }
                break;
            }
        }
        var sb = new System.Text.StringBuilder();
        sb.Append($"entry {(_entry != null)} '{(_entry != null ? _entryLabel.text : "")}' open {_open} pause {_pause} session {CoopSettings.Session ?? "-"} role {CoopSettings.RoleValue} on {CoopSettings.On} steam {SteamLobby.Ready()} ownLobby {SteamLobby.OwnLobby()} launchLobby {SteamLobby.LaunchLobby()} want {SteamLobby.WantFriend}/{SteamLobby.WantLobby}");
        foreach (var f in SteamLobby.Friends()) sb.Append($" | {f.Name} {f.Id} inGame {f.InGame} lobby {f.Lobby}");
        return sb.ToString();
    }
}
