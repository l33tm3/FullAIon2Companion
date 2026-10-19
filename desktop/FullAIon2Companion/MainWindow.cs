using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FullAIon2Companion;
public sealed class MainWindow : Window
{
    readonly WebView2 view=new();
    readonly TextBlock actionStatus = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18,7,18,7) };
    readonly Border actionBanner = new() { Background = new SolidColorBrush(Color.FromRgb(25,49,57)), Visibility = Visibility.Collapsed };
    ProfileState? ownProfile;
    /// <summary>Where donations go: the app and the website are free, and whoever wants can support them here.</summary>
    internal const string DonateUrl="https://paypal.me/martincito45";
    readonly CaptureEngine engine=new();
    readonly AtlasLocator atlas=new();
    // Market prices (Mercado tab and the loot HUD), per server: aion2.exchange's and the player's own.
    readonly MarketPrices market=new();
    int marketServer;string marketKey="",boardKey="";(long Value,int Unpriced) sessionMarket;
    // The player's own pins and routes on the Mapa de Atreia, also drawn on the overlay's minimap.
    readonly AtlasMarks atlasMarks=new();int marksSent=-1;
    // The Clasificación tab: sent again when a ranking frame came, the character changed or the panel reloaded.
    string rankingKey="";string? rankCharacter;
    // «Grabar 2 minutos»: the recorder process (a second copy of the app with --record), until it ends.
    System.Diagnostics.Process? recorder;DateTime recorderStart;string recorderNote="";
    // The server the Mercado tab shows (Configuración-free: its own selector), else the character's.
    int MarketViewServer=>settings.MarketServer>0?settings.MarketServer:marketServer;
    readonly AppSettings settings=AppSettings.Load();
    readonly EventTimers timers;
    readonly CompanionClient companion;
    // Ownership by swapping a title in the game, confirmed by the server on the official profile (the panel's Verificar personaje).
    readonly TitleVerification titles;
    readonly LicenseGate license;
    // Every fight kept on this PC (Historial tab): the meter clears a fight when the next one starts.
    readonly FightHistory history = new();
    FieldBossBoard? fieldBosses;
    FarmObjective objective=null!;
    readonly DispatcherTimer assistTick=new(){Interval=TimeSpan.FromMilliseconds(200)};
    bool fieldBossDirty=true;
    readonly ResourceBook resourceBook=new();
    BreakdownWindow? breakdown;
    readonly OverlayWindow overlay;
    readonly DispatcherTimer tick=new(){Interval=TimeSpan.FromMilliseconds(500)};
    bool ready,updating,exiting;
    object? petList;
    int ticks;
    string message="La captura se limita a la conexión de AION 2. Tus combates quedan en este equipo.";
    [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr h,int id,uint mods,uint key);
    [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
    public MainWindow()
    {
        Title="FullAIon2Companion · "+AppVersion.Current;Width=1260;Height=860;MinWidth=950;MinHeight=650;
        Background=new SolidColorBrush(Color.FromRgb(12,18,25));
        // A panel over the game rather than a separate window: no system frame, always on top, opened and closed
        // from the overlay's gear. The top bar drags it; the edges resize it.
        WindowStyle=WindowStyle.None;Topmost=true;
        WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=47,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(0),UseAeroCaptionButtons=false});
        BorderBrush=new SolidColorBrush(Color.FromRgb(51,87,91));BorderThickness=new Thickness(1);
        // Always in the taskbar with its own icon: hidden, the app looked closed while it kept running.
        try{Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory,"assets","app.ico")));}catch{}
        // With the overlay on, the panel starts minimized in the taskbar: the gear or the taskbar icon brings it up.
        if(settings.OverlayVisible)WindowState=WindowState.Minimized;
        timers=new(settings);companion=new(settings);overlay=new(settings);
        titles=new(companion,()=>DateTime.UtcNow,async()=>{if(companion.Paired)await companion.Pull();});
        // Everything is free (2026-10-19, the user's decision; donations by PayPal instead of a subscription): the PvP card
        // and the minimap are on for everyone and the license is no longer asked for. LicenseGate stays, unused, for its tests.
        license=new(companion);
        companion.Revoked+=()=>message=companion.Status;
        // The pet collection goes to the account on every change (at most once a minute) when the device is paired.
        engine.Collection.Changed+=v=>{if(settings.SyncPets)_=companion.UploadPets(v);};
        overlay.BossCast+=_=>{if(settings.BossSounds)timers.PlayBossAlert();};
        overlay.OpenNpc+=id=>_=ShowNpc(id);
        overlay.SettingsRequested+=TogglePanel;
        overlay.NewsRequested+=OpenNews;
        timers.Alerted+=(name,color,at,starting)=>overlay.Toast(name,color,at,starting);
        overlay.Feature=_=>true;
        engine.FightEnded+=history.Save;
        fieldBosses=new(settings);
        engine.FieldBossSeen=(npc,alive,at)=>{fieldBosses.Observe(npc,alive,at);fieldBossDirty=true;};
        L.Language=settings.Language;AionDPS.Aion2.Protocol.Aion2SkillNames.Language=settings.Language;
        engine.PinnedPets=settings.PinnedPets.ToArray();
        overlay.Session=()=>engine.Session;
        overlay.SessionKinah=()=>engine.Inventory.KinahSession.Gained;
        overlay.SessionMarket=()=>sessionMarket;
        overlay.MarketPrice=item=>market.Unit(marketServer,item);
        overlay.RecentLoot=engine.Inventory.Recent;
        objective=new(settings);
        overlay.Objective=ObjectiveNow;
        objective.Reached+=o=>{overlay.Notice("Objetivo cumplido",$"{o.Target:N0} {(o.Kind=="kills"?"muertes":"kinah")} · {(int)(o.Elapsed/60)} min",false);timers.Play();};
        // The combat assistant counts down at 5 Hz, faster than the panel's refresh.
        overlay.AssistantBeep=timers.Play;
        overlay.EnvObjects=()=>engine.Cubes.Live;
        overlay.CubeGuide=engine.CubeView;
        assistTick.Tick+=(_,_)=>{var (b,c)=engine.Assistant.View(engine.Now);overlay.UpdateAssistant(b,c);overlay.UpdateCube(engine.CubeView());};
        // A hidden cube announced by the server: sound and a notification in the chosen corner.
        engine.Cubes.Found+=c=>Dispatcher.BeginInvoke(()=>{if(!settings.CubeAlerts)return;if(settings.CubeSound)timers.Play();overlay.Notice("Cubo oculto cerca","El servidor acaba de mostrar un cubo oculto. Sigue la flecha sobre el overlay.",false);});assistTick.Start();
        overlay.BreakdownRequested+=ToggleBreakdown;
        var root=new DockPanel();
        var bar=new DockPanel{Background=new SolidColorBrush(Color.FromRgb(18,26,35)),Height=47};
        var top=new StackPanel{Orientation=Orientation.Horizontal};
        var close=BarButton("Minimizar  —","Vuelve al juego; el panel queda en la barra de tareas y el overlay sigue activo");close.Click+=(_,_)=>TogglePanel();
        var quit=BarButton("Salir","Cierra FullAIon2Companion y su overlay");quit.Click+=(_,_)=>{exiting=true;Close();};
        quit.Foreground=new SolidColorBrush(Color.FromRgb(0xFF,0x8A,0x65));
        var right=new StackPanel{Orientation=Orientation.Horizontal};right.Children.Add(quit);right.Children.Add(close);
        DockPanel.SetDock(right,Dock.Right);bar.Children.Add(right);bar.Children.Add(top);
        top.Children.Add(new TextBlock{Text="⚙  FullAIon2Companion",Foreground=Brushes.White,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(18,0,30,0)});
        // The website pages (Mapa de Atreia, Mi cuenta) open inside this window and have no way back to the panel: this does.
        backToPanel=BarButton("←  Volver al panel","Vuelve al panel de FullAIon2Companion");backToPanel.Foreground=new SolidColorBrush(Color.FromRgb(0x78,0xDC,0xD3));backToPanel.FontWeight=FontWeights.SemiBold;backToPanel.Visibility=Visibility.Collapsed;
        backToPanel.Click+=async(_,_)=>{siteLinkRequested=null;try{await Open("https://companion.local/ui/index.html");ShowAction("De vuelta en el panel.");}catch(Exception ex){ShowAction(ex.Message);}};
        top.Children.Add(backToPanel);
        DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);root.Children.Add(view);Content=root;
        actionBanner.Child=actionStatus;DockPanel.SetDock(actionBanner,Dock.Top);root.Children.Remove(view);root.Children.Add(actionBanner);root.Children.Add(view);
        // Capture and overlay first: the panel's WebView takes several seconds to start and the overlay used to wait for it.
        Loaded+=async(_,_)=>{try{engine.Start();}catch(Exception ex){message=ex.Message;}tick.Tick+=async(_,_)=>await Refresh();tick.Start();await Initialize();};
        SourceInitialized+=(_,_)=>{HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(Hook);RegisterHotkeys();};
        IsVisibleChanged+=(_,_)=>PanelStateChanged();
        StateChanged+=(_,_)=>PanelStateChanged();
        engine.IdleSeconds=settings.CombatIdleSeconds;engine.BossOnly=settings.BossOnlyDamage;
        // What each creature gave when it died, kept between sessions: the loot panel of creatures the catalog has no table for.
        engine.Observed.Open(Path.Combine(Program.DataDirectory,"loot-observed.json"));LootBook.Live=engine.Observed;
        // Market prices: aion2.exchange's, with its authors' permission (Aion2Exchange), and the player's own.
        market.Open(Path.Combine(Program.DataDirectory,"market-prices.json"));MarketPrices.Live=market;MarketPrices.Source=new Aion2Exchange();
        atlasMarks.Open(Path.Combine(Program.DataDirectory,"atlas-marks.json"));overlay.OwnMarks=atlasMarks.Of;
        engine.Rankings.Open(Path.Combine(Program.DataDirectory,"rankings.json"));engine.Rankings.ImportFrom(Path.Combine(Program.DataDirectory,"rare-frames.log"));
        overlay.AbyssRank=()=>engine.Rankings.Abyss(marketServer,rankCharacter);
        // The panel's X minimizes to the taskbar; closing it from the taskbar (already minimized) quits, like "Salir".
        Closing+=(_,e)=>{if(exiting)return;if(WindowState==WindowState.Minimized){exiting=true;return;}e.Cancel=true;TogglePanel();};
        Closed+=(_,_)=>{tick.Stop();assistTick.Stop();if(engine.EndedFight(force:true) is { } last)history.Save(last);license.Dispose();engine.Collection.Flush();engine.Observed.Flush();engine.Dispose();overlay.Close();var h=new WindowInteropHelper(this).Handle;for(int id=1;id<=Hotkeys.Actions.Length;id++)UnregisterHotKey(h,id);view.Dispose();
            // Hidden helper windows (fight details, news, boss panel) would keep the app alive: quit for real.
            Application.Current.Shutdown();};
        engine.Combat+=hit=>companion.Add(new{type="combat",at=CompanionClient.Utc(hit.Timestamp),eventId=Guid.NewGuid().ToString("N"),sourceId=hit.SourceObjectId.ToString(),targetId=hit.TargetObjectId.ToString(),skillId=hit.SkillId,damage=hit.IsHeal?0:hit.Amount,healing=hit.IsHeal?hit.Amount:0,critical=hit.IsCritical});
        engine.Death+=(id,npc,at)=>companion.Add(new{type="death",at=CompanionClient.Utc(at),eventId=Guid.NewGuid().ToString("N"),entityId=id.ToString(),npcId=npc});
    }
    // The shortcuts of Configuración > Atajos (by default Ctrl+Alt+F overlay, M move, B drops, P panel).
    IntPtr Hook(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled)
    {
        if(msg!=0x312)return IntPtr.Zero;
        switch(Hotkeys.ActionOf(wp.ToInt32()))
        {
            case "overlay":overlay.Requested=!overlay.Requested;break;
            case "move":ToggleMove();break;
            case "drops":overlay.ToggleDrops();break;
            case "panel":TogglePanel();break;
            case "reset":engine.ResetFight();message="Comenzó un nuevo registro de combate";break;
            case "minimap":overlay.ToggleMinimap();break;
            case "breakdown":ToggleBreakdown(0);break;
            default:return IntPtr.Zero;
        }
        handled=true;return IntPtr.Zero;
    }
    void RegisterHotkeys()
    {
        var hwnd=new WindowInteropHelper(this).Handle;if(hwnd==IntPtr.Zero)return;
        var busy=new List<string>();
        foreach(string action in Hotkeys.Actions)
        {
            int id=Hotkeys.IdOf(action);UnregisterHotKey(hwnd,id);
            if(settings.Hotkeys.GetValueOrDefault(action) is {Length:>0} combo&&Hotkeys.TryParse(combo,out uint mods,out uint vk)&&!RegisterHotKey(hwnd,id,mods,vk))busy.Add(combo);
        }
        if(busy.Count>0)message="Otra aplicación ya usa "+string.Join(", ",busy)+". Elige otra combinación en Configuración > Atajos.";
    }
    async Task Open(string url)
    {
        if(!ready)throw new InvalidOperationException("El panel todavía está iniciando. Intenta otra vez en unos segundos.");
        var uri=new Uri(url);
        // Inside the companion page a tab switch is enough; reloading would drop the open dialog and scroll position.
        if(view.Source?.Host=="companion.local"&&uri.Host=="companion.local")await view.ExecuteScriptAsync("window.openTab?.("+JsonSerializer.Serialize(uri.Fragment.TrimStart('#'))+")");
        else
        {
            var loaded=new TaskCompletionSource<bool>();
            void Done(object? s,CoreWebView2NavigationCompletedEventArgs e){view.CoreWebView2.NavigationCompleted-=Done;loaded.TrySetResult(e.IsSuccess);}
            view.CoreWebView2.NavigationCompleted+=Done;view.Source=uri;
            try{if(!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(25)))throw new InvalidOperationException("No se pudo cargar la página. Revisa tu conexión.");}
            finally{view.CoreWebView2.NavigationCompleted-=Done;}
        }
    }
    // The panel shows its own notification; the strip is only for the website pages (map, account), which have none.
    void ShowAction(string text){message=text;actionStatus.Text=text;actionBanner.Visibility=ready&&view.Source?.Host=="companion.local"?Visibility.Collapsed:Visibility.Visible;}
    async Task OpenOwnProfile()
    {
        var s=engine.Snapshot();var identity=engine.CapturedIdentity;
        var name=identity?.Name??s.Character;
        if(string.IsNullOrWhiteSpace(name))throw new InvalidOperationException("La captura aún no identificó tu personaje. Entra al juego o cambia de zona para recibir su ficha.");
        ShowAction("Buscando el perfil público de "+name+"…");
        // A public profile is presentation only: cached/inferred identity never selects a Plus license.
        ownProfile=PlayerProfiles.Get(name,identity?.ServerId>0?identity.Value.ServerId:s.LocalServerId??settings.LastServerId,PlayerProfiles.RegionOf(settings.RegionOffset));
        await Open("https://companion.local/ui/index.html#profile");
        await Refresh();
        ShowAction("Perfil de "+name+" abierto. Los datos oficiales se completan al recibirlos.");
    }
    static Button BarButton(string label,string? tip)
    {
        var button=new Button{Content=label,ToolTip=tip,Background=Brushes.Transparent,Foreground=Brushes.White,BorderThickness=new Thickness(0),Padding=new Thickness(14,8,14,8),Margin=new Thickness(3,7,3,7),Cursor=System.Windows.Input.Cursors.Hand};
        WindowChrome.SetIsHitTestVisibleInChrome(button,true);
        return button;
    }
    bool PanelShown=>IsVisible&&WindowState!=WindowState.Minimized;
    void PanelStateChanged(){overlay.PanelOpen=PanelShown;if(PanelShown)overlay.AttachPanel(this);}
    /// <summary>The gear: show the panel over the game, or minimize it to the taskbar and hand the focus back to the game.</summary>
    void TogglePanel()
    {
        if(PanelShown)
        {
            WindowState=WindowState.Minimized;
            var game=System.Diagnostics.Process.GetProcessesByName("AION2").FirstOrDefault();
            if(game is not null){SetForegroundWindow(game.MainWindowHandle);game.Dispose();}
            return;
        }
        // Show() runs IsVisibleChanged, which attaches the panel to the game (or makes it topmost when that is off).
        Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();
    }
    void ToggleMove(){overlay.SetClickThrough(!overlay.ClickThrough);if(!overlay.ClickThrough)overlay.Requested=true;}
    async Task ShowNpc(int id)
    {
        if(!ready)return;
        if(!IsVisible)Show();
        if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;
        Activate();
        if(view.Source?.Host!="companion.local"){view.Source=new Uri("https://companion.local/ui/index.html");await Task.Delay(900);}
        await view.ExecuteScriptAsync("window.showNpc?.("+JsonSerializer.Serialize(Catalog.Npc(id))+")");
    }
    async Task Initialize()
    {
        try
        {
            var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(Program.DataDirectory,"WebView"));
            await view.EnsureCoreWebView2Async(env);
            view.CoreWebView2.SetVirtualHostNameToFolderMapping("companion.local",AppContext.BaseDirectory,CoreWebView2HostResourceAccessKind.DenyCors);
            view.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
            view.CoreWebView2.WebMessageReceived+=OnMessage;
            view.CoreWebView2.NavigationStarting+=(_,e)=>{var uri=new Uri(e.Uri);if(uri.Host is not("companion.local" or "governanceai.me" or "aion2.plaync.com")) {e.Cancel=true;message="El enlace externo se ha bloqueado en el panel del juego";}};
            view.CoreWebView2.NewWindowRequested+=(_,e)=>{e.Handled=true;var uri=new Uri(e.Uri);if(uri.Host=="governanceai.me")view.Source=uri;};
            // The lists the panel only gets when they change (Mercado, marks) go again to a panel just loaded.
            view.CoreWebView2.NavigationCompleted+=(_,_)=>{marketKey=boardKey=rankingKey="";marksSent=-1;};
            view.Source=new Uri("https://companion.local/ui/index.html");ready=true;
        }
        catch(Exception ex){message=ex.Message;File.AppendAllText(Path.Combine(Program.DataDirectory,"errors.log"),ex+"\n");if(!ready)Content=new TextBlock{Text="No se pudo iniciar WebView2: "+ex.Message,Foreground=Brushes.White,Margin=new Thickness(30),TextWrapping=TextWrapping.Wrap};}
    }
    async Task PullQuietly(){try{await companion.Pull();}catch{}}
    /// <summary>
    /// «Grabar 2 minutos» in the panel: recording (seconds left), then where the file went and its size, or why it failed
    /// (no game connection, for one). Until 2026-10-18 the button said nothing after the first message.
    /// </summary>
    object? RecordingView()
    {
        if(recorder is null)return null;
        if(!recorder.HasExited){int left=Math.Max(0,120-(int)(DateTime.Now-recorderStart).TotalSeconds+10);return new{state="recording",left};}
        if(recorderNote.Length==0)
        {
            string result=Path.Combine(Program.DataDirectory,"record-result.json"),error=Path.Combine(Program.DataDirectory,"record-error.txt");
            try
            {
                if(File.Exists(result)&&JsonDocument.Parse(File.ReadAllText(result)).RootElement.GetProperty("file").GetString() is {} file&&File.Exists(file))
                    recorderNote=$"ok|{Path.GetFileName(file)}|{new FileInfo(file).Length}";
                else recorderNote="error|"+(File.Exists(error)?File.ReadAllText(error).Trim():"La grabación terminó sin guardar nada.");
            }
            catch(Exception ex){recorderNote="error|"+ex.Message;}
        }
        var parts=recorderNote.Split('|');
        return parts[0]=="ok"?new{state="done",file=parts[1],bytes=long.Parse(parts[2]),folder=Path.Combine(Program.DataDirectory,"captures")}:(object)new{state="failed",reason=parts[1]};
    }
    // ---- Linking from the website open in the panel (WebPairing): the page hands over a pairing code, never sees the token.
    DateTime? siteLinkRequested;bool sitePairing;
    async Task OnSiteMessage(string source,string json)
    {
        if(WebPairing.Parse(source,json) is not { } m)return;
        if(m.Kind=="query"){PostToSite(WebPairing.State(companion.Paired,siteLinkRequested is { } at&&DateTime.UtcNow-at<WebPairing.RequestLife));return;}
        if(sitePairing)return; // the page waits for the answer of the first one
        if(companion.Paired){PostToSite(WebPairing.Result(false,"Esta app ya está vinculada. Para usar otra cuenta, desconéctala antes en Configuración → Cuenta."));return;}
        sitePairing=true;bool ok;string text;
        try{ShowAction("Vinculando la app con tu cuenta…");await companion.Pair(m.Code!);ok=true;text="App vinculada con tu cuenta de Daeva Companion.";}
        // The token was stored but the first pull failed (no connection, a page instead of data): linked all the same.
        catch(Exception ex) when(companion.Paired){ok=true;text="App vinculada con tu cuenta. "+ex.Message;}
        catch(Exception ex){ok=false;text=ex.Message;}
        finally{sitePairing=false;}
        message=text;ShowAction(text);PostToSite(WebPairing.Result(ok,text));
        if(!ok)return;
        siteLinkRequested=null;
        if(settings.SyncPets)_=UploadAllPets();
        // Back to Configuración → Cuenta, where the link shows, once the page had time to say it.
        await Task.Delay(1500);
        if(view.Source is { } src&&WebPairing.FromSite(src.AbsoluteUri))
        {
            try{await Open("https://companion.local/ui/index.html");await view.ExecuteScriptAsync("window.openTab?.('settings','account')");ShowAction(text);}catch(Exception ex){ShowAction(ex.Message);}
        }
    }
    async Task UploadAllPets(){foreach(var c in engine.Collection.All())await companion.UploadPets(c);}
    // Only to a document of the site: what it gets is the linked state and the outcome of its own request.
    void PostToSite(string json){try{if(ready&&view.Source is { } s&&WebPairing.FromSite(s.AbsoluteUri))view.CoreWebView2.PostWebMessageAsJson(json);}catch{}}
    Diagnostics Diagnose()
    {
        string capture=engine.Connection is null?"unavailable":engine.Snapshot().Paused?"idle":"capturing";
        string sync=companion.State switch{CloudState.Connected=>"ok",CloudState.Revoked=>"error",_=>"pending"};
        string code=capture=="unavailable"?"CAPTURE_NO_GAME":sync=="error"?"SYNC_REVOKED":sync=="pending"?"SYNC_OFFLINE":"";
        return new(AppVersion.Current,settings.UpdateChannel,capture,sync,code);
    }
    // Administration notices: shown in the panel while active, and once as a corner notification.
    void ShowAnnouncements()
    {
        foreach(var a in ActiveAnnouncements())
        {
            if(!settings.SeenAnnouncements.Add(a.Id))continue;
            overlay.Notice(a.Title,a.Body,a.Level is "warning" or "maintenance" or "critical");
            settings.Save();
        }
    }
    IEnumerable<Announcement> ActiveAnnouncements()=>(companion.LastPull?.Announcements??[]).Where(a=>a.ActiveAt(DateTime.UtcNow));
    string? updateFile;string updateStatus="";
    Release? LatestRelease()=>companion.LastPull?.Releases?.GetValueOrDefault(settings.UpdateChannel);
    // ---- Novedades: the update available and the site's notices, in one window with an unread dot on the overlay strip.
    NewsWindow? newsWindow;
    readonly HashSet<string> newsToasted=[];
    List<NewsItem> NewsItems()
    {
        var items=new List<NewsItem>();
        var r=LatestRelease();
        if(r is not null&&AppVersion.IsNewer(r.Version,AppVersion.Current))
            items.Add(new("update:"+r.Version,"update",$"Versión {r.Version}",string.IsNullOrWhiteSpace(r.Notes)?"Sin notas de versión.":r.Notes,"info",
                $"Tienes la {AppVersion.Current} · {r.Bytes/1048576.0:0.0} MB"+(r.CreatedUtc is DateTime c?$" · publicada el {c.ToLocalTime():dd/MM/yyyy}":"")));
        foreach(var a in ActiveAnnouncements())items.Add(new("notice:"+a.Id,"notice",a.Title,a.Body,a.Level));
        if(items.Count==0&&r is not null&&r.Version==AppVersion.Current&&!string.IsNullOrWhiteSpace(r.Notes))items.Add(new("current:"+r.Version,"current",$"Versión {r.Version}",r.Notes,"info","La que tienes instalada"));
        return items;
    }
    void OpenNews()
    {
        var items=NewsItems();
        newsWindow??=new NewsWindow{Install=InstallUpdate};
        overlay.AttachPanel(newsWindow);
        newsWindow.Show(items,AppVersion.Current);
        foreach(var i in items.Where(i=>i.Kind!="current"&&!settings.NewsSeen.Contains(i.Key)))settings.NewsSeen.Add(i.Key);
        while(settings.NewsSeen.Count>200)settings.NewsSeen.RemoveAt(0);
        settings.Save();UpdateNewsBadge();
    }
    int newsUnread;
    void UpdateNewsBadge()
    {
        var unread=NewsItems().Where(i=>i.Kind!="current"&&!settings.NewsSeen.Contains(i.Key)).ToList();
        newsUnread=unread.Count;
        overlay.SetNewsUnread(unread.Count>0,unread.Count>0?"Novedades: "+string.Join(" · ",unread.Select(u=>u.Title)):null);
        // One corner notification per new item, the first time it shows up.
        foreach(var u in unread.Where(u=>newsToasted.Add(u.Key)))
            overlay.Notice(u.Kind=="update"?"Actualización disponible":"Novedades",(u.Kind=="update"?u.Title+" lista para instalar":u.Title)+" · pulsa el megáfono del overlay para verla",false);
    }
    /// <summary>"Actualizar ahora": only on the user's click. Downloads from the site if needed, checks size and SHA-256 again
    /// right before running it, then runs the installer silently (it closes this app, replaces the program files and opens
    /// the new version; the data folder is not touched) and quits.</summary>
    async Task InstallUpdate()
    {
        var rel=LatestRelease()??throw new InvalidOperationException("No hay una versión publicada en este canal");
        if(!AppVersion.IsNewer(rel.Version,AppVersion.Current))throw new InvalidOperationException("Ya tienes la última versión");
        newsWindow?.SetStatus($"Descargando {rel.Version}…");
        if(updateFile is null||!File.Exists(updateFile))updateFile=await companion.DownloadReleaseAsync(rel,Path.Combine(Program.DataDirectory,"updates"));
        var info=new FileInfo(updateFile);string hash;
        using(var stream=File.OpenRead(updateFile))hash=Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream)).ToLowerInvariant();
        if(info.Length!=rel.Bytes||hash!=rel.Sha256){File.Delete(updateFile);updateFile=null;throw new InvalidOperationException("El instalador descargado no coincide con el publicado; se borró. Vuelve a intentarlo.");}
        newsWindow?.SetStatus($"Instalando {rel.Version}: la app se cerrará y volverá a abrirse sola en unos segundos.");
        updateStatus=$"Instalando {rel.Version}…";
        await Task.Delay(800);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(updateFile,"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NOCANCEL"){UseShellExecute=true});
        exiting=true;Close();
    }
    object UpdateView()
    {
        var r=LatestRelease();
        bool newer=r is not null&&AppVersion.IsNewer(r.Version,AppVersion.Current);
        return new{current=AppVersion.Current,channel=settings.UpdateChannel,paired=companion.Paired,latest=r is null?null:new{r.Version,r.Notes,r.Bytes,created=r.CreatedUtc},newer,
            status=updateStatus.Length>0?updateStatus:!companion.Paired?"Vincula el dispositivo para consultar las versiones publicadas":r is null?"No hay versiones publicadas en este canal":newer?$"Hay una versión nueva: {r.Version}":"Tienes la última versión de este canal",
            downloaded=updateFile is not null&&File.Exists(updateFile)};
    }

    void ToggleBreakdown(int actor){breakdown??=new BreakdownWindow(engine.Breakdown,settings);breakdown.Toggle(actor);}
    /// <summary>The character the capture shows, resolved to its official reference, handed to the challenge every tick.</summary>
    CharacterRef? ownReference;
    // Soporte inside the app: the account's tickets and the one open, refreshed on demand (never on a timer).
    JsonElement? supportList, supportOpen;
    string supportStatus="";
    async Task SupportRefresh(string? open=null)
    {
        var list=await companion.SupportTicketsAsync();
        supportList=list.TryGetProperty("tickets",out var t)?t.Clone():null;
        string? id=open??(supportOpen is {} o&&o.TryGetProperty("id",out var oid)?oid.GetString():null);
        supportOpen=id is null?null:(await companion.SupportTicketAsync(id)).TryGetProperty("ticket",out var tk)?tk.Clone():null;
        supportStatus=$"Actualizado a las {DateTime.Now:HH:mm}";
    }
    // What the team needs to reproduce a problem, added under the user's message: no tokens, no packets, no personal data.
    string SupportContext()=>$"\n\n— FullAIon2Companion {AppVersion.Current} · {engine.Status} · {Environment.OSVersion.VersionString}";
    (int Id, string Name, int ServerId)? lastOwnIdentity;
    void UpdateVerification()
    {
        CharacterRef? observed=null;string? why=null;
        var current=engine.CapturedIdentity;
        if(current!=lastOwnIdentity)lastOwnIdentity=current;
        if(current is not { } id)why="La captura aún no vio la ficha de tu personaje en esta sesión: cambia de zona o teletranspórtate una vez. No vale el nombre configurado ni uno deducido.";
        else if(id.ServerId<=0)why="La captura todavía no confirmó el servidor de tu personaje.";
        else
        {
            var region=PlayerProfiles.RegionOf(settings.RegionOffset);
            var state=PlayerProfiles.Get(id.Name,id.ServerId,region);
            if(state.Card is {} card)observed=OwnLicenseIdentity.Resolve(id,card,region);
            if(observed is null)why=state.Error??$"Buscando la ficha oficial de {id.Name}…";
        }
        ownReference=observed;
        titles.UpdateContext(companion.Online,observed,current?.Name,why,companion.LastPull);
        _=titles.TickAsync();
    }
    ObjectiveView? ObjectiveNow()=>objective.View(engine.Session.Kills,engine.Inventory.KinahSession.Gained,engine.Inventory.LootValue,DateTime.Now);
    void RestartObjective()=>objective.Restart(engine.Session.Kills,engine.Inventory.KinahSession.Gained,engine.Inventory.LootValue);
    Button? backToPanel;
    async Task Refresh()
    {
        if(backToPanel is not null)backToPanel.Visibility=ready&&view.Source is {} src&&src.Host!="companion.local"?Visibility.Visible:Visibility.Collapsed;
        if(updating)return;updating=true;
        try
        {
            UpdateVerification();
            // A fight quiet for the idle time goes to the history at once (the next fight would clear it).
            if(ticks%4==2&&engine.EndedFight() is { } ended)_=Task.Run(()=>history.Save(ended));
            var snapshot=engine.Snapshot();var events=timers.Tick(DateTimeOffset.Now);
            // Only the own party: strangers fighting nearby in the open world do not belong in the ranking.
            // Without a roster yet (solo, or right after start) only the local player is listed.
            // Party names for the damage ranking: the troop (all groups, or only the own one, per Configuración) when the
            // game sent its roster, else the party list.
            var party=snapshot.Troop is { } troop?troop.Members.Where(m=>settings.OverlayTroopAll&&troop.IsTroop||m.Group==troop.OwnGroup).Select(m=>m.Name).ToList():(IReadOnlyCollection<string>)(snapshot.Party??[]);
            // The instance room's members count as the group too: someone who joined by the room finder may be missing from
            // the party list for a while (2026-10-13: Elymas, 5th of the room, left out of the ranking).
            if(snapshot.Room is { Members.Count: >0 } roomNow)party=party.Concat(roomNow.Members.Select(m=>m.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // Where the player is on the atlas: the Atlas of this panel (data.atlas) and the website's, every second.
            var here=atlas.Locate(snapshot);
            // The session's loot at Market prices, for the overlay (only items with a price count; the others are told apart).
            marketServer=snapshot.LocalServerId??settings.LastServerId;rankCharacter=snapshot.Character;
            // The Market of the character's server (loot HUD) and of the one the Mercado tab shows, asked again every 30 min.
            market.Want(marketServer);if(MarketViewServer!=marketServer)market.Want(MarketViewServer);
            {var gains=engine.Inventory.Loot;sessionMarket=(gains.Sum(g=>(market.Unit(marketServer,g.Item)??0)*g.Quantity),gains.Where(g=>market.Unit(marketServer,g.Item) is null&&!InventoryWatch.Bound(g.Item)).Select(g=>g.Item).Distinct().Count());}
            if(ticks%2==0&&ready&&view.Source is { } site&&WebPairing.FromSite(site.AbsoluteUri))PostToSite(JsonSerializer.Serialize(here));
            overlay.Update(snapshot,events.FirstOrDefault(),snapshot.Actors.Where(a=>a.Id==snapshot.LocalId&&snapshot.LocalId>0||engine.Entities.NpcIdOf(a.Id) is null&&(settings.PartyOnly?party.Contains(a.Name):!a.Name.StartsWith("Entidad ",StringComparison.Ordinal))).ToList());
            // Every 10 s: profiles of the players around, so a PvP target shows its card at once.
            if(ticks%20==0&&settings.OverlayDrops)PlayerProfiles.Prefetch(engine.RecentPlayers(30),snapshot.LocalServerId??settings.LastServerId,PlayerProfiles.RegionOf(settings.RegionOffset));
            // Every 5 s: follow the game if it opened a new connection (relog, reconnection, instance transfer).
            if(ticks%10==5||engine.Connection is null&&ticks%2==1){try{if(engine.CheckConnection())message="La captura siguió al juego a su nueva conexión";}catch(Exception ex){message=ex.Message;}}
            // Automatic pet sync: a first upload a minute after start (changes made while offline), then a retry every 5 min after a failure.
            if(ticks%600==120&&settings.SyncPets&&companion.Paired&&(companion.PetsPending||!companion.PetsUploadedOnce))foreach(var c in engine.Collection.All())await companion.UploadPets(c);
            // Account: pull every 20 s keeps the device active and checks the token is still valid (a 401 clears it).
            if(companion.Paired&&ticks%40==6)_=PullQuietly();
            // Voluntary diagnostics: version, channel and the capture / sync state, every 30 s and on changes.
            if(settings.ShareDiagnostics&&companion.Paired&&ticks%2==0)_=companion.SendDiagnosticsAsync(Diagnose());
            ShowAnnouncements();
            if(ticks%4==1)engine.UpdateCollection(snapshot.Character,snapshot.LocalServerId??settings.LastServerId,PlayerProfiles.RegionOf(settings.RegionOffset));
            if(++ticks%10==0){_ = companion.Flush();File.WriteAllText(Path.Combine(Program.DataDirectory,"live.json"),JsonSerializer.Serialize(snapshot));File.WriteAllText(Path.Combine(Program.DataDirectory,"account-live.json"),JsonSerializer.Serialize(new{at=DateTime.Now,state=companion.State.ToString(),companion.Status,pull=companion.LastPull,identity=engine.CapturedIdentity is { } ci?new{ci.Id,ci.Name,ci.ServerId}:null,identityDebug=engine.IdentityDebug,title=titles.View()}));}
            L.ApplyAll(this); // overlay and the other windows in the chosen language
            if(ticks%2==0)fieldBosses?.Tick(overlay,timers);
            if(ticks%4==3)UpdateNewsBadge();
            // Header chips: the next field boss (starred first), the next rift and the next Shugo event.
            {
                var chips=new List<(string,string,string,DateTime)>();
                if(fieldBosses?.Next() is { } fb)chips.Add(("♛",fb.Name.Length>16?fb.Name[..15]+"…":fb.Name,fb.Starred?"#E9C77E":"#C9B98F",fb.At));
                if(events.FirstOrDefault(e=>e.Id=="rift") is { } rift)chips.Add(("◈","Grieta",rift.Color,rift.At.LocalDateTime));
                if(events.FirstOrDefault(e=>e.Id=="defend-shugo") is { } shugo)chips.Add(("✦","Shugo",shugo.Color,shugo.At.LocalDateTime));
                overlay.SetHeaderTimers(chips);
            }
            if(view.Source?.Host!="companion.local")return;
            var mobs=engine.Entities.LiveMobs().GroupBy(m=>m.NpcId).Select(g=>new{id=g.Key,name=Catalog.NpcName(g.Key),count=g.Count(),record=Catalog.Npc(g.Key)}).ToList();
            var classes=snapshot.Actors.Select(a=>(a.Id,Class:engine.Entities.ClassOf(a.Id))).Where(x=>x.Class is not null).ToDictionary(x=>x.Id.ToString(),x=>x.Class);
            var lootAll=engine.Inventory.Loot;var sess=engine.Session;
            var farm=new{start=sess.Start,kills=sess.Kills,kinah=engine.Inventory.KinahSession.Gained,kinahNow=engine.Inventory.KinahSession.Now,lootValue=engine.Inventory.LootValue,
                loot=lootAll.TakeLast(60).Reverse(),top=lootAll.GroupBy(l=>l.Item).Select(g=>new{item=g.Key,name=g.First().Name,icon=g.First().Icon,grade=g.First().Grade,qty=g.Sum(x=>x.Quantity),value=g.Sum(x=>x.Value)}).OrderByDescending(x=>x.value).ThenByDescending(x=>x.qty).Take(12)};
            var cv=engine.CubeView();
            resourceBook.Update(snapshot.Character,snapshot.LocalServerId??settings.LastServerId,engine.Resources,engine.Inventory);
            // Captures for the website's tour (2026-10-19): a flag in the data folder asks for one copy of all the panel gets, heavy lists too.
            string dumpFlag=Path.Combine(Program.DataDirectory,"panel-dump.flag");bool dump=File.Exists(dumpFlag);
            if(dump){marketKey=boardKey=rankingKey="";marksSent=-1;}
            // The Mercado tab: sent when the inventory, a price or the server changed (it is the biggest list of the panel), and every 10 s.
            string mk=$"{engine.Inventory.Version}:{market.Version}:{marketServer}:{MarketViewServer}:{L.Language}:{market.Status}:{snapshot.Character}";
            object? marketView=null;if(mk!=marketKey||ticks%20==0){marketKey=mk;marketView=market.View(engine.Inventory,MarketViewServer,snapshot.Character,marketServer);}
            // «Todo el Mercado» (about 600 items): only when the server's board, the server list or the language changed.
            string bk=market.BoardKey(MarketViewServer)+":"+L.Language;object? boardView=null;if(bk!=boardKey){boardKey=bk;boardView=market.BoardView(MarketViewServer);}
            object? marksView=null;if(atlasMarks.Version!=marksSent){marksSent=atlasMarks.Version;marksView=atlasMarks.View();}
            string rk=$"{engine.Rankings.Version}:{snapshot.LocalServerId??settings.LastServerId}:{snapshot.Character}:{L.Language}";
            object? rankingView=null;if(rk!=rankingKey){rankingKey=rk;rankingView=engine.Rankings.View(snapshot.LocalServerId??settings.LastServerId,snapshot.Character);}
            var data=new{news=new{unread=newsUnread},market=marketView,marketBoard=boardView,atlasMarks=marksView,ranking=rankingView,recording=RecordingView(),support=new{list=supportList,open=supportOpen,status=supportStatus,paired=companion.Paired},resources=resourceBook.View(snapshot.Character,snapshot.LocalServerId??settings.LastServerId),farm,objective=ObjectiveNow(),cubes=new{cv.Active,cv.Distance,cv.Bearing,cv.Height,cv.SpotsNear,cv.NearestSpot,cv.NearestBearing,map=MinimapData.Map(cv.Map)?.Name,history=cv.History.Select(h=>new{h.At,h.GoneAt,h.LeftRangeAt,h.SeenFrom,h.Missing,h.EnvId,map=MinimapData.Map(h.Map)?.Name??h.Map,x=Math.Round(h.X/100),y=Math.Round(h.Y/100),z=Math.Round(h.Z/100)})},fieldBosses=ticks%4==0||fieldBossDirty?fieldBosses?.View():null,historyVersion=history.Version,classes,profile=ownProfile is null?null:new{stage=ownProfile.Stage,error=ownProfile.Error,card=ownProfile.Card},version=AppVersion.Current,update=UpdateView(),announcements=ActiveAnnouncements().Select(a=>new{a.Title,a.Body,a.Level}),diagnostics=companion.DiagnosticsStatus,features=new{pvp=license.Has("companionPvp"),minimap=license.Has("companionMinimap"),plan=license.View().Title},license=license.View(),minimapLayers=MinimapWindow.Layers.Select(l=>new{key=l.Key,label=l.Label,color=l.Color}),petList=petList??=PetTracker.All.Select(x=>new{id=x.Id,name=x.Name}).ToList(),snapshot,events,settings,petCollection=engine.Collection.Of(snapshot.Character,snapshot.LocalServerId??settings.LastServerId)??engine.Collection.All().OrderByDescending(c=>c.UpdatedAt).FirstOrDefault(),petsSync=companion.PetsStatus,cloud=new{state=companion.State.ToString(),pull=companion.LastPull,title=titles.View()},companion=companion.Status,paired=companion.Paired,message,overlay=overlay.Requested,locked=overlay.ClickThrough,assistantMoving=overlay.AssistantMoving,npc=snapshot.Target?.NpcId is int id?Catalog.Npc(id):null,mobs,alert=timers.LastAlert,atlas=here};
            string panelJson=UiContract.Serialize(data);
            if(dump){try{File.Delete(dumpFlag);File.WriteAllText(Path.Combine(Program.DataDirectory,"panel-dump.json"),panelJson);}catch{}}
            await view.ExecuteScriptAsync("window.renderCompanion?.("+panelJson+")");
            fieldBossDirty=false;
        }
        catch(Exception ex){message=ex.Message;}
        finally{updating=false;}
    }
    /// <summary>One setting from the Configuración tab, validated and applied right away.</summary>
    void Set(string key,JsonElement v)
    {
        switch(key)
        {
            case "combatIdle":settings.CombatIdleSeconds=engine.IdleSeconds=Math.Clamp(v.GetInt32(),5,120);break;
            case "partyOnly":settings.PartyOnly=v.GetBoolean();break;
            case "troopAll":settings.OverlayTroopAll=v.GetBoolean();break;
            case "bossOnly":settings.BossOnlyDamage=engine.BossOnly=v.GetBoolean();break;
            case "syncPets":settings.SyncPets=v.GetBoolean();break;
            case "opacity":settings.OverlayOpacity=Math.Clamp(v.GetDouble(),.3,1);overlay.ApplyAppearance();break;
            case "scale":settings.OverlayScale=Math.Clamp(v.GetDouble(),.8,1.6);overlay.ApplyAppearance();break;
            case "showParty":settings.OverlayShowParty=v.GetBoolean();break;
            case "showCasts":settings.OverlayShowCasts=v.GetBoolean();break;
            case "showOd":settings.OverlayShowOd=v.GetBoolean();break;
            case "odMax":settings.OdMax=Math.Clamp(v.GetInt32(),1,10000);break;
            case "showPet":settings.OverlayShowPet=v.GetBoolean();break;
            case "showFarm":settings.OverlayShowFarm=v.GetBoolean();break;
            case "showLootPrices":settings.OverlayShowLootPrices=v.GetBoolean();break;
            case "showRanking":settings.OverlayShowRanking=v.GetBoolean();break;
            case "overlayStyle":{string styleKey=v.GetString()??"";if(OverlayWindow.Styles.All(st=>st.Key!=styleKey))throw new InvalidOperationException("Estilo desconocido");settings.OverlayStyle=styleKey;overlay.ApplyOverlayStyle();break;}
            // The Mercado tab's server; the character's own is stored as 0, so it follows the character.
            case "marketServer":{int server=v.GetInt32();if(server is not 0 and not(>=1000 and <=9999))throw new InvalidOperationException("Servidor desconocido");settings.MarketServer=server==marketServer?0:server;marketKey=boardKey="";break;}
            case "showHeaderTimers":settings.OverlayShowHeaderTimers=v.GetBoolean();break;
            case "cubeAlerts":settings.CubeAlerts=v.GetBoolean();break;
            case "cubeSound":settings.CubeSound=v.GetBoolean();break;
            case "assistant":settings.AssistantVisible=v.GetBoolean();break;
            case "assistantBuffs":settings.AssistantShowBuffs=v.GetBoolean();break;
            case "assistantCooldowns":settings.AssistantShowCooldowns=v.GetBoolean();break;
            case "assistantSound":settings.AssistantSound=v.GetBoolean();break;
            case "assistantScale":settings.AssistantScale=Math.Clamp(v.GetDouble(),.6,2);overlay.AssistantLook();break;
            case "fieldBossStar":{int npc=v.GetInt32();if(!settings.FieldBossStars.Remove(npc))settings.FieldBossStars.Add(npc);fieldBossDirty=true;break;}
            case "fieldBossAlerts":settings.FieldBossAlerts=v.GetBoolean();break;
            case "fieldBossLead":settings.FieldBossLeadMinutes=Math.Clamp(v.GetInt32(),1,30);break;
            case "language":{string lang=v.GetString()??"es";if(lang is not("es" or "en" or "pt"))throw new InvalidOperationException("Idioma no disponible");settings.Language=lang;L.Language=lang;AionDPS.Aion2.Protocol.Aion2SkillNames.Language=lang;break;}
            case "showEvent":settings.OverlayShowEvent=v.GetBoolean();break;
            case "showDrops":if(settings.OverlayDrops!=v.GetBoolean())overlay.ToggleDrops();break;
            case "showMinimap":if(settings.MinimapVisible!=v.GetBoolean())overlay.ToggleMinimap();break;
            case "focusOnly":settings.OverlayAttachToGame=v.GetBoolean();overlay.Reattach();break;
            case "bossSounds":settings.BossSounds=v.GetBoolean();break;
            case "eventSounds":settings.Sounds=v.GetBoolean();break;
            case "leadMinutes":settings.LeadMinutes=Math.Clamp(v.GetInt32(),1,30);break;
            case "volume":settings.Volume=Math.Clamp(v.GetDouble(),0,1);break;
            case "region":settings.RegionOffset=v.GetInt32();break;
            case "bossVoice":settings.BossAlertVoice=v.GetString() is "es" or "en" or "alarm" ? v.GetString()! : "es";settings.Save();timers.PlayBossAlert();break;
            case "bossPacePlace":settings.BossPacePlace=v.GetString() is "overlay" or "window" or "both" ? v.GetString()! : "both";break;
            case "bossPaceBars":settings.BossPaceBars=v.GetBoolean();break;
            case "bossPaceReset":settings.BossPaceLeft=settings.BossPaceTop=null;break;
            case "eventToasts":settings.EventToasts=v.GetBoolean();break;
            case "toastCorner":settings.EventToastCorner=v.GetString() is "top-right" or "top-left" or "bottom-right" or "bottom-left" ? v.GetString()! : "top-right";break;
            case "toastTest":overlay.Toast("Invasión","#71DED6",DateTimeOffset.Now.AddMinutes(5),false);break;
            case "castKind":
            {
                string kind=v.GetProperty("kind").GetString()!,what=v.GetProperty("what").GetString()!;bool on=v.GetProperty("on").GetBoolean();
                if(kind is not("dungeon" or "field" or "elite" or "target"))throw new InvalidOperationException("Tipo desconocido");
                var set=what=="sound"?settings.CastSoundKinds:settings.CastBannerKinds;
                if(on)set.Add(kind);else set.Remove(kind);break;
            }
            case "castPlace":settings.CastAlertPlace=v.GetString() is "center" or "overlay" or "both" ? v.GetString()! : "center";break;
            case "castTest":overlay.TestCastAlert();break;
            case "updateChannel":settings.UpdateChannel=v.GetString() is "stable" ? "stable" : "beta";updateStatus="";updateFile=null;break;
            case "shareDiagnostics":settings.ShareDiagnostics=v.GetBoolean();break;
            case "minimapOpacity":settings.MinimapOpacity=Math.Clamp(v.GetDouble(),.3,1);overlay.MinimapLook();break;
            case "minimapIcons":settings.MinimapIconScale=Math.Clamp(v.GetDouble(),.6,2);overlay.MinimapLook();break;
            case "minimapFiltersOpen":settings.MinimapFiltersOpen=v.GetBoolean();overlay.MinimapLook();break;
            case "minimapReset":overlay.MinimapReset();break;
            case "minimapLayer":
            {
                string layer=v.GetProperty("layer").GetString()!;bool on=v.GetProperty("on").GetBoolean();
                if(!MinimapWindow.Layers.Any(l=>l.Key==layer))throw new InvalidOperationException("Capa desconocida");
                if(on){if(!settings.MinimapFilters.Contains(layer))settings.MinimapFilters.Add(layer);}else settings.MinimapFilters.Remove(layer);
                overlay.MinimapLook();break;
            }
            default:throw new InvalidOperationException("Ajuste desconocido: "+key);
        }
    }
    async void OnMessage(object? sender,CoreWebView2WebMessageReceivedEventArgs e)
    {
        if(WebPairing.FromSite(e.Source)){try{await OnSiteMessage(e.Source,e.WebMessageAsJson);}catch(Exception ex){ShowAction(ex.Message);}return;}
        if(!e.Source.StartsWith("https://companion.local/ui/",StringComparison.Ordinal))return;
        string? requestId=null;bool ok=true;
        try
        {
            using var document=JsonDocument.Parse(e.WebMessageAsJson);var p=document.RootElement;string? command=p.GetProperty("command").GetString();
            requestId=p.TryGetProperty("requestId",out var rid)?rid.GetString():null;
            ShowAction("Realizando acción…");
            string completed="Acción completada.";
            switch(command)
            {
                case "overlay":overlay.Requested=!overlay.Requested;message=overlay.Requested?"Overlay visible":"Overlay oculto";break;
                case "lock":ToggleMove();message=overlay.ClickThrough?"Overlay fijado":"Puedes mover el overlay";break;
                case "pause":engine.SetPaused(!engine.Snapshot().Paused);message=engine.Snapshot().Paused?"Registro de combate pausado":"Registro de combate reanudado";break;
                case "reset":engine.ResetFight();message="Comenzó un nuevo registro de combate";break;
                case "history-list":
                {
                    string? q=p.TryGetProperty("query",out var qv)?qv.GetString():null;bool bosses=p.TryGetProperty("bosses",out var bv)&&bv.ValueKind==JsonValueKind.True;
                    var list=history.List(q,bosses);
                    await view.ExecuteScriptAsync("window.renderHistory?.("+JsonSerializer.Serialize(list)+")");
                    message=list.Count==0?"No hay combates guardados con ese filtro.":list.Count==1?"1 combate en el historial.":$"{list.Count} combates en el historial.";break;
                }
                case "history-open":
                {
                    var fight=history.Open(p.GetProperty("id").GetString()??"")??throw new InvalidOperationException("Ese combate ya no está en el historial.");
                    await view.ExecuteScriptAsync("window.showFight?.("+JsonSerializer.Serialize(fight)+")");
                    message=$"{fight.Summary.Target} · {fight.Summary.Start:dd/MM HH:mm}";break;
                }
                case "pet-pin":
                {
                    int pet=p.GetProperty("id").GetInt32();
                    if(PetTracker.NameOf(pet) is not { } petName)throw new InvalidOperationException("Mascota desconocida.");
                    if(!settings.PinnedPets.Remove(pet)){if(settings.PinnedPets.Count>=6)throw new InvalidOperationException("Puedes fijar hasta 6 mascotas en el overlay.");settings.PinnedPets.Add(pet);}
                    settings.Save();engine.PinnedPets=settings.PinnedPets.ToArray();
                    message=settings.PinnedPets.Contains(pet)?$"{petName} fijada en el overlay.":$"{petName} ya no está fijada.";break;
                }
                case "pet-sources":
                {
                    int pet=p.GetProperty("id").GetInt32();var sources=PetTracker.SourcesOf(pet);
                    await view.ExecuteScriptAsync($"window.showPetSources?.({pet},"+JsonSerializer.Serialize(sources)+")");
                    message=sources.Count==0?"Sin fuentes conocidas.":$"{sources.Count} criaturas dan almas de {PetTracker.NameOf(pet)}.";break;
                }
                case "market-price":
                {
                    int item=p.GetProperty("item").GetInt32();long price=p.GetProperty("price").ValueKind==JsonValueKind.Number?p.GetProperty("price").GetInt64():0;
                    market.SetManual(MarketViewServer,item,price);marketKey="";
                    message=price>0?$"Precio guardado: {InventoryWatch.Info(item).Name} a {price:N0} kinah en este servidor.":$"Precio de {InventoryWatch.Info(item).Name} borrado.";break;
                }
                case "atlas-marks":
                {
                    var (pins,routes)=atlasMarks.Set(p.GetProperty("map").GetString()??"",p.GetProperty("pins"),p.GetProperty("routes"));marksSent=atlasMarks.Version;
                    message=$"Tus marcas de este mapa: {pins} {(pins==1?"pin":"pines")} · {routes} {(routes==1?"ruta":"rutas")}. También se ven en el minimapa.";break;
                }
                case "farm-reset":engine.ResetSession();objective.SessionReset();message="Sesión de farmeo reiniciada.";break;
                case "history-delete":history.Delete(p.GetProperty("id").GetString()??"");message="Combate borrado del historial.";break;
                case "capture":engine.Start();message=engine.Status;break;
                case "export":var path=Path.Combine(Program.DataDirectory,"fights",DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);engine.Export(path);message="Combate exportado en "+path;break;
                case "sound":settings.Sounds=!settings.Sounds;settings.Save();if(settings.Sounds)timers.Play();break;
                case "sound-test":timers.Play();message="Sonido de prueba reproducido";break;
                case "boss-sound":settings.BossSounds=!settings.BossSounds;settings.Save();if(settings.BossSounds)timers.PlayBossAlert();break;
                case "boss-sound-test":timers.PlayBossAlert();message="Alerta de habilidad de jefe reproducida";break;
                case "event":string id=p.GetProperty("id").GetString()!;if(!settings.SelectedEvents.Remove(id))settings.SelectedEvents.Add(id);settings.Save();break;
                case "goal-set":{string kind=p.GetProperty("kind").GetString() is { } k&&FarmObjective.Kinds.Contains(k)?k:"kills";settings.GoalKind=kind;settings.GoalTarget=Math.Clamp(p.GetProperty("target").GetInt64(),0,1_000_000_000_000);settings.GoalMinutes=Math.Clamp(p.GetProperty("minutes").GetInt32(),0,24*60);settings.Save();RestartObjective();message=settings.GoalTarget>0?"Objetivo definido.":"Objetivo quitado.";break;}
                case "assistant-move":overlay.AssistantMoving=!overlay.AssistantMoving;message=overlay.AssistantMoving?"Arrastra el asistente de combate donde quieras y pulsa «Fijar» al terminar.":"Asistente de combate fijado.";break;
                case "assistant-reset":overlay.AssistantReset();message="El asistente de combate volvió a su posición inicial.";break;
                case "cube-clear":engine.Cubes.ClearHistory();message="Historial de cubos borrado.";break;
                case "goal-restart":RestartObjective();message="Objetivo reiniciado.";break;
                case "goal-clear":settings.GoalTarget=0;settings.Save();message="Objetivo quitado.";break;
                case "goal":settings.FarmGoal=Math.Clamp(p.GetProperty("value").GetInt32(),1,100000);settings.Save();break;
                case "volume":settings.Volume=Math.Clamp(p.GetProperty("value").GetDouble(),0,1);settings.Save();break;
                case "region":settings.RegionOffset=p.GetProperty("value").GetInt32();settings.Save();break;
                case "pair":await companion.Pair(p.GetProperty("code").GetString()!.Trim());message=companion.Status;if(settings.SyncPets)foreach(var c in engine.Collection.All())await companion.UploadPets(c);break;
                case "pets-upload":{if(!companion.Paired)throw new InvalidOperationException("Vincula el dispositivo para subir tus mascotas.");var all=engine.Collection.All();if(all.Count==0)throw new InvalidOperationException("Aún no hay colección: entra al juego con el personaje para que llegue la lista");foreach(var c in all)await companion.UploadPets(c);message=companion.PetsStatus;if(companion.PetsPending)throw new InvalidOperationException(message);break;}
                case "pull":await companion.Pull();message="Preferencias y estado descargados. "+companion.Status;break;
                case "pair-web":
                    // The website in this panel: sign in there and its «Vincular esta app» hands the code over (OnSiteMessage).
                    if(companion.Paired)throw new InvalidOperationException("Esta app ya está vinculada con tu cuenta.");
                    siteLinkRequested=DateTime.UtcNow;await Open(WebPairing.Site+"/#home");
                    message="Inicia sesión en Daeva Companion y pulsa «Vincular esta app». «← Volver al panel» te trae de vuelta.";break;
                case "disconnect":companion.Disconnect();message=companion.Status;break;
                case "title-start":await titles.StartAsync();message=titles.View().Message;break;
                case "title-check":await titles.CheckAsync();message=titles.View().Message;break;
                case "title-cancel":await titles.CancelAsync();message=titles.View().Message;break;
                case "license-check":if(companion.Paired)await companion.Pull();license.RecheckSoon();await license.TickAsync();message=license.View().Detail;break;
                case "telemetry":if(!companion.Paired)throw new InvalidOperationException("Vincula el dispositivo antes de compartir");settings.ShareTelemetry=!settings.ShareTelemetry;settings.Save();break;
                case "npc":
                {
                    int npcId=p.GetProperty("id").GetInt32();var record=Catalog.Npc(npcId);
                    // A variant without its own loot shows its sibling's (same creature, nearest level), labelled.
                    var sibling=record is {} r0&&!(r0.TryGetProperty("drops",out var own)&&own.ValueKind==JsonValueKind.Array&&own.GetArrayLength()>0)&&Catalog.DropsSibling(npcId) is int sid?Catalog.Npc(sid):null;
                    await view.ExecuteScriptAsync("window.showNpc("+JsonSerializer.Serialize(record)+","+JsonSerializer.Serialize(sibling)+")");break;
                }
                case "map":view.Source=new Uri("https://governanceai.me/#map");break;
                case "own-profile":await OpenOwnProfile();break;
                case "open-map":await Open("https://companion.local/ui/index.html#atlas");message="Mapa de Atreia abierto.";break;
                // The website's Atlas: dungeon maps, notes and routes the Atlas of the app does not have.
                case "open-web-map":await Open("https://governanceai.me/#map");message="Atlas de la web abierto en el panel.";break;
                case "pet-track":engine.Pets.Track(p.GetProperty("id").GetInt32(),p.GetProperty("level").GetInt32(),p.GetProperty("progress").GetInt32());message="Siguiendo la mascota";break;
                case "party-forget":engine.ForgetParty();message="Grupo olvidado: se volverá a detectar cuando el juego lo envíe";break;
                case "pet-stop":engine.Pets.Track(0,0,0);message="Seguimiento de mascota detenido";break;
                case "set":Set(p.GetProperty("key").GetString()!,p.GetProperty("value"));settings.Save();break;
                case "hotkey":
                {
                    string action=p.GetProperty("action").GetString()!,combo=p.GetProperty("combo").GetString()!.Trim();
                    if(!Hotkeys.Actions.Contains(action))break;
                    if(combo.Length>0&&!Hotkeys.TryParse(combo,out _,out _))throw new InvalidOperationException("Usa Ctrl, Alt o Win junto a una letra, un número o F1-F24");
                    if(combo.Length>0&&settings.Hotkeys.Any(h=>h.Key!=action&&h.Value.Equals(combo,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException(combo+" ya está asignado a otra acción");
                    settings.Hotkeys[action]=combo;settings.Save();message=combo.Length>0?"Atajo guardado: "+combo:"Atajo quitado";RegisterHotkeys();break;
                }
                case "open-data":System.Diagnostics.Process.Start("explorer.exe",Program.DataDirectory);break;
                case "license-subscribe":System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://governanceai.me/#home"){UseShellExecute=true});message="En la web: Cuenta → Suscripción. El pago se hace allí; Plus se activa aquí cuando el servidor confirme la licencia de tu personaje.";break;
                case "support":System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://governanceai.me/support/"){UseShellExecute=true});break;
                case "support-refresh":if(!companion.Paired)throw new InvalidOperationException("Vincula la app con tu cuenta (Mi cuenta) para escribir al soporte");await SupportRefresh();message="Consultas actualizadas";break;
                case "support-open":{string ticket=p.GetProperty("id").GetString()??"";await SupportRefresh(ticket);message="Consulta abierta";break;}
                case "support-close":supportOpen=null;message="Consulta cerrada";break;
                case "support-create":
                {
                    if(!companion.Paired)throw new InvalidOperationException("Vincula la app con tu cuenta (Mi cuenta) para escribir al soporte");
                    string subject=(p.GetProperty("subject").GetString()??"").Trim(),text=(p.GetProperty("message").GetString()??"").Trim(),category=p.GetProperty("category").GetString()??"desktop";
                    if(subject.Length<5)throw new InvalidOperationException("Escribe un asunto de al menos 5 letras");
                    if(text.Length<5)throw new InvalidOperationException("Cuéntanos qué pasó en el mensaje");
                    bool withCharacter=p.TryGetProperty("character",out var wc)&&wc.ValueKind==JsonValueKind.True;
                    var sent=await companion.SupportCreateAsync(subject[..Math.Min(160,subject.Length)],category,(text+SupportContext())[..Math.Min(5000,text.Length+SupportContext().Length)],withCharacter?ownReference:null);
                    await SupportRefresh(sent.TryGetProperty("id",out var nid)?nid.GetString():null);
                    message=sent.TryGetProperty("message",out var m)?m.GetString()??"Consulta enviada":"Consulta enviada";break;
                }
                case "support-reply":
                {
                    string ticket=p.GetProperty("id").GetString()??"",text=(p.GetProperty("message").GetString()??"").Trim();
                    if(text.Length<5)throw new InvalidOperationException("Escribe tu respuesta (al menos 5 letras)");
                    await companion.SupportReplyAsync(ticket,text[..Math.Min(5000,text.Length)]);await SupportRefresh(ticket);message="Respuesta enviada";break;
                }
                case "news-open":OpenNews();break;
                case "donate":System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DonateUrl){UseShellExecute=true});message="Gracias por apoyarnos: la donación se hace en PayPal, en tu navegador.";break;
                case "update-install":await InstallUpdate();break;
                case "update-check":updateStatus="";await companion.Pull();message=LatestRelease() is {} found && AppVersion.IsNewer(found.Version,AppVersion.Current)?$"Versión {found.Version} disponible":"No hay una versión más nueva en este canal";break;
                case "update-download":
                {
                    var rel=LatestRelease()??throw new InvalidOperationException("No hay una versión publicada en este canal");
                    updateStatus=$"Descargando {rel.Version}…";
                    try{updateFile=await companion.DownloadReleaseAsync(rel,Path.Combine(Program.DataDirectory,"updates"));updateStatus=$"Versión {rel.Version} descargada y verificada (tamaño y SHA-256)";}
                    catch(Exception ex){updateFile=null;updateStatus=ex.Message;throw;}
                    break;
                }
                case "update-open":
                    // Only on the user's click, and only the file that passed the size and SHA-256 check.
                    if(updateFile is null||!File.Exists(updateFile))throw new InvalidOperationException("Descarga primero la versión nueva");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(updateFile){UseShellExecute=true});break;
                case "record":
                {
                    if(recorder is {HasExited:false})throw new InvalidOperationException("Ya hay una grabación en curso.");
                    // The recorder says how it went in record-result.json (or record-error.txt): the old ones must not count.
                    foreach(var old in new[]{"record-result.json","record-error.txt"})try{File.Delete(Path.Combine(Program.DataDirectory,old));}catch{}
                    recorder=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!,"--record 120"){UseShellExecute=false,CreateNoWindow=true})
                        ??throw new InvalidOperationException("No se pudo empezar la grabación.");
                    recorderStart=DateTime.Now;recorderNote="";
                    message="Grabando 2 minutos de la conexión del juego. Haz ahora en el juego lo que quieras grabar.";break;
                }
                default:throw new InvalidOperationException("Esta acción no está disponible: "+command);
            }
            if(message=="Realizando acción…")message=completed;
        }
        catch(Exception ex){ok=false;message=ex.Message;}
        finally
        {
            ShowAction(message);
            if(ready&&view.Source?.Host=="companion.local"&&requestId is {Length:>0 and <100})
            {
                try{view.CoreWebView2.PostWebMessageAsJson(UiContract.Serialize(new{type="action-result",requestId,ok,message}));}catch{}
            }
        }
    }
}
