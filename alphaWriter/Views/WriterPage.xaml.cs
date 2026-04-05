using alphaWriter.Models;
using alphaWriter.ViewModels;

namespace alphaWriter.Views
{
    public partial class WriterPage : ContentPage
    {
        private WriterViewModel _viewModel;
        private bool _editorReady;
        private bool _heatMapActive;
        private Scene? _currentEditorScene; // The scene currently loaded in the WebView
        private int? _pendingScrollSentenceIndex; // Set before a cross-scene note navigation; consumed by LoadSceneAsync
        private CancellationTokenSource? _sceneChangeCts; // Cancels a mid-flight scene change when a newer one supersedes it

        public WriterPage(WriterViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = viewModel;

            _viewModel.SelectedSceneChanged += OnSelectedSceneChanged;
            _viewModel.EntitiesChanged += () => _ = PushEntitiesAsync();
            _viewModel.ReportsPanelShown += () => _ = LoadReportAsync();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _viewModel.InitializeAsync();
            AttachGlobalDragSuppressor();
        }

        protected override async void OnDisappearing()
        {
            base.OnDisappearing();
            var scene = _currentEditorScene;
            if (scene is not null)
            {
                try { await FlushEditorContentAsync(scene); }
                catch { /* preserve existing content */ }
            }
        }

        private bool _dragSuppressorAttached;

        // ── WebView events ────────────────────────────────────────────────────

        private async void OnEditorNavigated(object? sender, WebNavigatedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[EDITOR] OnEditorNavigated: result={e.Result}, url={e.Url}");
            if (e.Result == WebNavigationResult.Success &&
                e.Url.EndsWith("editor.html", StringComparison.OrdinalIgnoreCase))
            {
                _editorReady = true;
                // Load content if a scene is already selected, and track it so
                // subsequent contentChanged notifications are not silently dropped.
                if (_viewModel.SelectedScene is not null)
                {
                    System.Diagnostics.Debug.WriteLine($"[EDITOR] OnEditorNavigated: loading scene '{_viewModel.SelectedScene.Title}'");
                    await LoadSceneAsync(_viewModel.SelectedScene);
                    _currentEditorScene = _viewModel.SelectedScene;
                }
            }
        }

        private async void OnEditorNavigating(object? sender, WebNavigatingEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[EDITOR] OnEditorNavigating: url={e.Url}");
            if (e.Url.StartsWith("alphawriter://contentChanged", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;

                // Save to the scene that is actually loaded in the editor,
                // not SelectedScene — a stale notification can arrive after a scene switch.
                var targetScene = _currentEditorScene;
                System.Diagnostics.Debug.WriteLine($"[EDITOR] contentChanged: _currentEditorScene={targetScene?.Title ?? "NULL"}");
                if (targetScene is null) return;

                var raw = await EditorWebView.EvaluateJavaScriptAsync("getContent()");
                System.Diagnostics.Debug.WriteLine($"[EDITOR] contentChanged getContent raw={raw?.Substring(0, Math.Min(raw?.Length ?? 0, 100)) ?? "NULL"}");
                if (raw is null) return; // WebView unavailable — don't wipe content
                var content = DecodeJsString(raw);
                var wcRaw = await EditorWebView.EvaluateJavaScriptAsync("getWordCount()");
                int? jsWordCount = null;
                if (int.TryParse(wcRaw, out var wc)) jsWordCount = wc;

                targetScene.Content = content;
                System.Diagnostics.Debug.WriteLine($"[EDITOR] contentChanged: saved {content.Length} chars, wc={jsWordCount} to '{targetScene.Title}'");
                if (jsWordCount.HasValue)
                    targetScene.WordCount = jsWordCount.Value;
                _viewModel.UpdateWordCountDisplay(targetScene);
                _viewModel.DebouncedSave();
            }
            else if (e.Url.StartsWith("alphawriter://entityClicked", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                var id   = ExtractQueryParam(e.Url, "id");
                var type = ExtractQueryParam(e.Url, "type");
                if (id is not null && type is not null)
                    _viewModel.ShowEntityPreview(id, type);
            }
        }

        private static string? ExtractQueryParam(string url, string param)
        {
            var qi = url.IndexOf('?');
            if (qi < 0) return null;
            foreach (var part in url[(qi + 1)..].Split('&'))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0] == param)
                    return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }

        // ── Scene change: update WebView content ──────────────────────────────

        private async void OnSelectedSceneChanged(Scene? scene)
        {
            System.Diagnostics.Debug.WriteLine($"[EDITOR] OnSelectedSceneChanged: scene={scene?.Title ?? "NULL"}, _editorReady={_editorReady}, _currentEditorScene={_currentEditorScene?.Title ?? "NULL"}");
            if (!_editorReady) return;

            // Cancel any in-flight scene change and create a new token for this one.
            // This prevents a rapid null→scene transition (caused by SelectedChapter change
            // nulling SelectedScene before the real scene is set) from racing: the null
            // handler's setContent('') must not fire after the real scene has already loaded.
            _sceneChangeCts?.Cancel();
            var cts = _sceneChangeCts = new CancellationTokenSource();

            // Flush the outgoing scene's content before loading the new one.
            // Clear _currentEditorScene first so stale navigations are ignored.
            var outgoing = _currentEditorScene;
            _currentEditorScene = null;

            if (outgoing is not null)
            {
                System.Diagnostics.Debug.WriteLine($"[EDITOR] Flushing outgoing scene '{outgoing.Title}', current Content length={outgoing.Content?.Length ?? -1}");
                await FlushEditorContentAsync(outgoing);
                System.Diagnostics.Debug.WriteLine($"[EDITOR] After flush, outgoing '{outgoing.Title}' Content length={outgoing.Content?.Length ?? -1}");
            }

            // If a newer scene-change handler has already started, bail out.
            // The newer handler is responsible for loading its scene; clearing the editor
            // here would race with—and clobber—whatever it has already loaded.
            if (cts.IsCancellationRequested) return;

            if (scene is null)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await EditorWebView.EvaluateJavaScriptAsync("setContent('')"));
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[EDITOR] Loading scene '{scene.Title}', Content length={scene.Content?.Length ?? -1}, Content preview='{scene.Content?.Substring(0, Math.Min(scene.Content?.Length ?? 0, 80))}'");
            await LoadSceneAsync(scene);
            if (!cts.IsCancellationRequested)
            {
                _currentEditorScene = scene;
                System.Diagnostics.Debug.WriteLine($"[EDITOR] Scene '{scene.Title}' loaded, _currentEditorScene set");
            }
        }

        private async Task FlushEditorContentAsync(Scene scene)
        {
            try
            {
                var raw = await EditorWebView.EvaluateJavaScriptAsync(
                    "(flushContent(), getContent())");
                System.Diagnostics.Debug.WriteLine($"[EDITOR] FlushEditor: raw result type={raw?.GetType().Name ?? "NULL"}, length={raw?.Length ?? -1}, preview='{raw?.Substring(0, Math.Min(raw?.Length ?? 0, 100))}'");
                if (raw is not null)
                {
                    var content = DecodeJsString(raw);
                    System.Diagnostics.Debug.WriteLine($"[EDITOR] FlushEditor: decoded content length={content.Length}, preview='{content.Substring(0, Math.Min(content.Length, 100))}'");
                    scene.Content = content;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[EDITOR] FlushEditor: raw was NULL, preserving existing content");
                }

                var wcRaw = await EditorWebView.EvaluateJavaScriptAsync("getWordCount()");
                System.Diagnostics.Debug.WriteLine($"[EDITOR] FlushEditor: wordCount raw='{wcRaw}'");
                if (int.TryParse(wcRaw, out var wc))
                    scene.WordCount = wc;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EDITOR] FlushEditor EXCEPTION: {ex.Message}");
            }

            _viewModel.TriggerSave();
        }

        private async Task LoadSceneAsync(Scene scene)
        {
            if (!_editorReady) return;

            var content    = scene.Content ?? string.Empty;
            var contentArg = System.Text.Json.JsonSerializer.Serialize(content);
            System.Diagnostics.Debug.WriteLine($"[EDITOR] LoadScene: '{scene.Title}', content length={content.Length}");

            // 1. Set entities — pass the JSON array directly as a JS expression
            //    (no double-serialization / JSON.parse needed)
            try
            {
                var entitiesJson = _viewModel.GetEntityManifestJson(); // e.g. [{"id":"...","name":"John",...}]
                var entResult = await EditorWebView.EvaluateJavaScriptAsync($"setEntities({entitiesJson})");
                System.Diagnostics.Debug.WriteLine($"[EDITOR] LoadScene setEntities: {entResult} entities loaded");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EDITOR] LoadScene setEntities EXCEPTION: {ex.Message}");
            }

            // 2. Set content (separate call — setContent applies entity highlighting internally)
            await EditorWebView.EvaluateJavaScriptAsync($"setContent({contentArg})");

            // 3. Safety net: explicitly re-highlight after both entities and content are set.
            //    This catches any case where setContent's built-in highlighting didn't fire
            //    (e.g. _entities wasn't visible due to a prior WebView2 error state).
            try
            {
                await EditorWebView.EvaluateJavaScriptAsync("refreshHighlighting()");
            }
            catch { /* non-critical */ }

            // 4. Reapply heat map overlays if they were active before the scene switch.
            await ReapplyHeatMapIfActiveAsync();

            // 5. If a note-navigation jump is pending (set by NavigateToNoteAsync before
            //    the scene switch), scroll to and flash-highlight the target sentence.
            if (_pendingScrollSentenceIndex.HasValue)
            {
                var idx = _pendingScrollSentenceIndex.Value;
                _pendingScrollSentenceIndex = null;
                try { await EditorWebView.EvaluateJavaScriptAsync($"scrollToSentence({idx})"); }
                catch { /* non-critical */ }
            }
        }

        /// <summary>
        /// Re-pushes the current entity manifest while a scene is live in the editor.
        /// Called when entity names/aliases change (EntitiesChanged event).
        /// </summary>
        private async Task PushEntitiesAsync()
        {
            if (!_editorReady) return;
            var entitiesJson = _viewModel.GetEntityManifestJson();
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await EditorWebView.EvaluateJavaScriptAsync($"setEntities({entitiesJson})"));
        }

        // ── Report WebView ──────────────────────────────────────────────────
        // The report HTML is generated entirely in C# and loaded via
        // HtmlWebViewSource.  This bypasses WebView2's aggressive file
        // caching (the same issue that caused the editor.html regression)
        // and eliminates all timing / readiness race conditions because the
        // entity data is embedded directly in the HTML — no post-load
        // EvaluateJavaScriptAsync push required.

        private string? _cachedVisNetworkJs;
        private string? _cachedChartJs;

        private async Task LoadReportAsync()
        {
            try
            {
                if (_viewModel.ActiveReportType == "NLP")
                {
                    // Cache Chart.js on first use (~200 KB)
                    if (_cachedChartJs is null)
                    {
                        try
                        {
                            using var stream = await FileSystem.OpenAppPackageFileAsync("chartjs.min.js");
                            using var reader = new StreamReader(stream);
                            _cachedChartJs = await reader.ReadToEndAsync();
                        }
                        catch
                        {
                            _cachedChartJs = string.Empty; // fallback: charts won't render but page still loads
                        }
                    }

                    var dataJson = _viewModel.BuildNlpReportJson();
                    System.Diagnostics.Debug.WriteLine(
                        $"[REPORT] LoadReportAsync NLP: dataJson length={dataJson.Length}");
                    var html = BuildNlpReportHtml(_cachedChartJs ?? string.Empty, dataJson);
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        ReportWebView.Source = new HtmlWebViewSource { Html = html });
                }
                else
                {
                    // Cache the 490 KB vis-network library on first use
                    if (_cachedVisNetworkJs is null)
                    {
                        using var stream = await FileSystem.OpenAppPackageFileAsync("vis-network.min.js");
                        using var reader = new StreamReader(stream);
                        _cachedVisNetworkJs = await reader.ReadToEndAsync();
                    }

                    var dataJson = _viewModel.BuildEntityRelationshipJson();
                    System.Diagnostics.Debug.WriteLine(
                        $"[REPORT] LoadReportAsync Entities: dataJson length={dataJson.Length}, " +
                        $"visJs length={_cachedVisNetworkJs.Length}");
                    var html = BuildReportHtml(_cachedVisNetworkJs, dataJson);
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        ReportWebView.Source = new HtmlWebViewSource { Html = html });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[REPORT] LoadReportAsync failed: {ex}");
            }
        }

        /// <summary>
        /// Builds a fully self-contained HTML document with vis-network JS
        /// inlined and entity data embedded — nothing external, nothing cached.
        /// Uses absolute positioning (not flexbox) so the canvas container has
        /// rigid pixel dimensions that survive vis.js interaction redraws.
        /// </summary>
        private static string BuildReportHtml(string visJs, string dataJson)
        {
            // CSS — absolute positioning prevents layout reflow from stealing
            // the container's dimensions during vis.js hover / click redraws.
            const string css = @"
* { margin:0; padding:0; box-sizing:border-box; }
html, body { width:100%; height:100%; overflow:hidden;
    background:#1E1E28; color:#E8E4DC;
    font-family:'Segoe UI',system-ui,sans-serif; }
#controls { position:absolute; top:0; left:0; right:0; height:42px;
    display:flex; align-items:center; gap:8px; padding:0 16px;
    background:#22222A; border-bottom:1px solid #3A3A48; z-index:2; }
#controls .label { font-size:12px; color:#9E9AA0; margin-right:4px; }
.filter-btn { display:inline-flex; align-items:center; gap:5px;
    padding:4px 12px; border:1px solid #3A3A48; border-radius:4px;
    background:#2A2A35; color:#E8E4DC; font-size:12px;
    cursor:pointer; transition:all .15s ease; user-select:none; }
.filter-btn:hover { background:#2E2E3A; }
.filter-btn.active { border-color:var(--accent); background:#313145; }
.filter-btn .dot { width:8px; height:8px; border-radius:50%;
    background:var(--accent); }
#info { margin-left:auto; font-size:11px; color:#5A5A6A; }
#network { position:absolute; top:42px; left:0; right:0; bottom:0; }
#empty { position:absolute; top:42px; left:0; right:0; bottom:0;
    display:none; justify-content:center; align-items:center;
    text-align:center; color:#5A5A6A; font-size:14px;
    font-style:italic; padding:40px; line-height:1.6; }
div.vis-tooltip { position:absolute; background:#2A2A35 !important;
    border:1px solid #4A4A5C !important; color:#E8E4DC !important;
    font-size:12px !important; border-radius:4px !important;
    padding:6px 10px !important;
    box-shadow:0 4px 12px rgba(0,0,0,.5) !important;
    pointer-events:none; z-index:10; }
";

            // JS application logic (vis-network.min.js is prepended separately)
            const string appJs = @"
var _allNodes=[],_allEdges=[];
var _filters={character:true,location:true,item:true};
var _network=null;
var _nodesDS=new vis.DataSet();
var _edgesDS=new vis.DataSet();
var TYPE_COLORS={
    character:{background:'#A89EC9',border:'#8A7EB0',
        highlight:{background:'#C0B4E0',border:'#A89EC9'},
        hover:{background:'#B8AAD8',border:'#A89EC9'}},
    location:{background:'#7EC8A4',border:'#5EAE84',
        highlight:{background:'#A0E0C0',border:'#7EC8A4'},
        hover:{background:'#90D8B4',border:'#7EC8A4'}},
    item:{background:'#C8B07A',border:'#B09860',
        highlight:{background:'#E0C890',border:'#C8B07A'},
        hover:{background:'#D8C088',border:'#C8B07A'}}
};
var TYPE_LABELS={character:'Character',location:'Location',item:'Item'};

function initNetwork(){
    var c=document.getElementById('network');
    _network=new vis.Network(c,{nodes:_nodesDS,edges:_edgesDS},{
        autoResize:false,
        nodes:{shape:'dot',
            font:{color:'#E8E4DC',size:13,face:""'Segoe UI',system-ui,sans-serif""},
            borderWidth:2,
            shadow:{enabled:true,color:'rgba(0,0,0,0.4)',size:8,x:0,y:2}},
        edges:{color:{inherit:'both',opacity:0.5},
            smooth:{enabled:true,type:'continuous',roundness:0.3},
            hoverWidth:1.5,
            scaling:{min:1,max:8,label:{enabled:false}}},
        interaction:{dragNodes:true,hover:true,tooltipDelay:200,
            hideEdgesOnDrag:false,multiselect:true},
        physics:{enabled:true,solver:'forceAtlas2Based',
            forceAtlas2Based:{gravitationalConstant:-40,centralGravity:0.008,
                springLength:140,springConstant:0.06,damping:0.4,avoidOverlap:0.3},
            stabilization:{enabled:true,iterations:600,updateInterval:30,fit:true}}
    });
    _network.once('stabilizationIterationsDone',function(){
        _network.setOptions({physics:{enabled:false}});
    });
    // Manually size the canvas to its container once, since autoResize is off.
    var rect=c.getBoundingClientRect();
    _network.setSize(rect.width+'px',rect.height+'px');
    _network.redraw(); _network.fit();
}

function setData(data){
    if(typeof data==='string'){try{data=JSON.parse(data);}catch(e){data={nodes:[],edges:[]};}}
    _allNodes=(data.nodes||[]).map(function(n){
        var colors=TYPE_COLORS[n.type]||TYPE_COLORS.character;
        var sc=n.scenes||0;
        var tip=n.label+'  \u2014  '+(TYPE_LABELS[n.type]||n.type);
        if(sc>0) tip+='\nAppears in '+sc+' scene'+(sc!==1?'s':'');
        else tip+='\nNot yet tagged to any scene';
        return{id:n.id,label:n.label,type:n.type,color:colors,
            font:{color:'#E8E4DC'},title:tip};
    });
    _allEdges=(data.edges||[]).map(function(e,i){
        return{id:'e'+i,from:e.from,to:e.to,value:e.value,
            title:e.value+' shared scene'+(e.value!==1?'s':'')};
    });
    var wSum={};
    _allEdges.forEach(function(e){
        wSum[e.from]=(wSum[e.from]||0)+e.value;
        wSum[e.to]=(wSum[e.to]||0)+e.value;
    });
    var mx=1; for(var k in wSum){if(wSum[k]>mx)mx=wSum[k];}
    _allNodes.forEach(function(n){
        var w=wSum[n.id]||0;
        n.size=w>0?14+Math.round(16*(w/mx)):12;
    });
    applyFilters();
}

function toggleFilter(type,btn){
    _filters[type]=!_filters[type];
    btn.classList.toggle('active',_filters[type]);
    applyFilters();
}

function applyFilters(){
    var vt={}; for(var t in _filters){if(_filters[t])vt[t]=true;}
    var vn=_allNodes.filter(function(n){return vt[n.type];});
    var vi={}; vn.forEach(function(n){vi[n.id]=true;});
    var ve=_allEdges.filter(function(e){return vi[e.from]&&vi[e.to];});
    _nodesDS.clear(); _edgesDS.clear();
    _nodesDS.add(vn); _edgesDS.add(ve);
    var has=vn.length>0;
    document.getElementById('network').style.visibility=has?'visible':'hidden';
    document.getElementById('empty').style.display=has?'none':'flex';
    document.getElementById('info').textContent=
        vn.length+' entities, '+ve.length+' connections';
    if(_network&&has){
        _network.setOptions({physics:{enabled:true}});
        _network.once('stabilizationIterationsDone',function(){
            _network.setOptions({physics:{enabled:false}});
        });
        _network.fit({animation:{duration:300,easingFunction:'easeInOutQuad'}});
    }
}
";

            const string bodyHtml = @"
<div id=""controls"">
    <span class=""label"">Show:</span>
    <button class=""filter-btn active"" style=""--accent:#A89EC9;""
            onclick=""toggleFilter('character',this)"">
        <span class=""dot""></span> Characters</button>
    <button class=""filter-btn active"" style=""--accent:#7EC8A4;""
            onclick=""toggleFilter('location',this)"">
        <span class=""dot""></span> Locations</button>
    <button class=""filter-btn active"" style=""--accent:#C8B07A;""
            onclick=""toggleFilter('item',this)"">
        <span class=""dot""></span> Items</button>
    <span id=""info""></span>
</div>
<div id=""network""></div>
<div id=""empty"">No entities defined in this book yet.<br/>
    Add characters, locations, or items in the sidebar tabs.</div>
";

            return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>"
                 + "<style>" + css + "</style></head><body>"
                 + bodyHtml
                 + "<script>" + visJs + "</" + "script>"
                 + "<script>" + appJs
                 + "initNetwork();setData(" + dataJson + ");"
                 + "</" + "script></body></html>";
        }

        // ── NLP Report HTML ──────────────────────────────────────────────────
        /// <summary>
        /// Builds a self-contained NLP analysis report with SVG charts.
        /// Includes: pacing heatmap, style consistency chart, emotion distribution,
        /// and a categorized notes list.
        /// </summary>
        private static string BuildNlpReportHtml(string chartJs, string dataJson)
        {
            const string css = @"
* { margin:0; padding:0; box-sizing:border-box; }
html, body { width:100%; height:100%; overflow-y:auto; overflow-x:hidden;
    background:#1E1E28; color:#E8E4DC;
    font-family:'Segoe UI',system-ui,sans-serif; font-size:13px; }
.container { padding:16px 20px 40px; max-width:1200px; margin:0 auto; }
h2 { font-size:15px; font-weight:600; color:#A89EC9; margin:24px 0 12px; padding-bottom:6px;
    border-bottom:1px solid #3A3A48; }
h2:first-child { margin-top:0; }
.chart-area { background:#22222A; border:1px solid #3A3A48; border-radius:6px;
    padding:12px; margin-bottom:16px; overflow-x:auto; }
.heatmap { display:flex; gap:2px; flex-wrap:wrap; align-items:flex-end; min-height:60px; }
.hm-cell { min-width:28px; flex:1; border-radius:3px; position:relative;
    cursor:default; transition:opacity .15s; }
.hm-cell:hover { opacity:.8; }
.hm-label { font-size:9px; color:#9E9AA0; text-align:center; margin-top:2px;
    white-space:nowrap; overflow:hidden; text-overflow:ellipsis; max-width:60px; }
.bar-chart { display:flex; gap:3px; align-items:flex-end; height:120px; }
.bar-col { display:flex; flex-direction:column; align-items:center; flex:1; min-width:20px; height:100%; justify-content:flex-end; }
.bar { width:100%; border-radius:2px 2px 0 0; min-height:2px; transition:height .3s; }
.bar-label { font-size:9px; color:#9E9AA0; margin-top:3px; text-align:center;
    white-space:nowrap; overflow:hidden; text-overflow:ellipsis; max-width:60px; }
.emotion-row { display:flex; gap:2px; height:18px; border-radius:3px; overflow:hidden; margin-bottom:3px; }
.emotion-seg { height:100%; transition:width .3s; }
.emotion-legend { display:flex; gap:12px; flex-wrap:wrap; margin-top:8px; }
.legend-item { display:flex; align-items:center; gap:4px; font-size:11px; color:#9E9AA0; }
.legend-dot { width:8px; height:8px; border-radius:50%; }
.empty-state { color:#5A5A6A; font-style:italic; text-align:center; padding:30px; }
.tooltip { position:absolute; background:#2A2A35; border:1px solid #4A4A5C;
    color:#E8E4DC; font-size:11px; border-radius:4px; padding:4px 8px;
    pointer-events:none; z-index:10; white-space:nowrap;
    box-shadow:0 4px 12px rgba(0,0,0,.5); display:none; }
.stats-grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(180px,1fr)); gap:8px; margin-bottom:16px; }
.stat-card { background:#22222A; border:1px solid #3A3A48; border-radius:6px; padding:10px 12px; }
.stat-value { font-size:20px; font-weight:700; color:#A89EC9; }
.stat-label { font-size:11px; color:#9E9AA0; margin-top:2px; }

/* ── Triage panel ─────────────────────────────────────────────────────── */
.triage-grid { display:grid; grid-template-columns:repeat(3,1fr); gap:12px; margin-bottom:16px; }
@media (max-width:600px) { .triage-grid { grid-template-columns:1fr; } }
.triage-col { background:#22222A; border:1px solid #3A3A48; border-radius:6px; padding:12px; }
.triage-heading { display:flex; align-items:center; gap:8px; margin-bottom:10px; }
.triage-dot { width:10px; height:10px; border-radius:50%; flex-shrink:0; }
.triage-title { font-size:13px; font-weight:600; }
.triage-count { margin-left:auto; font-size:11px; color:#9E9AA0; }
.triage-item { border-left:2px solid transparent; padding:6px 8px;
    margin-bottom:5px; border-radius:0 4px 4px 0; background:#1A1A22;
    cursor:pointer; }
.triage-item:hover { background:#252530; }
.triage-cat { font-size:10px; color:#9E9AA0; margin-bottom:2px; }
.triage-msg { font-size:11px; color:#C8C4BC; line-height:1.45; }
.triage-crumb { font-size:10px; color:#5A5A6A; margin-top:3px; }

/* ── Character fingerprint ────────────────────────────────────────────── */
.char-panel { display:flex; gap:20px; flex-wrap:wrap; align-items:flex-start; }
.radar-wrapper { position:relative; flex-shrink:0; }
.char-toggles { display:flex; gap:6px; flex-wrap:wrap; margin-bottom:10px; }
.char-toggle-btn { font-size:11px; padding:3px 10px; border-radius:12px;
    border:2px solid transparent; cursor:pointer; background:transparent;
    color:#E8E4DC; transition:opacity .15s; }
.char-toggle-btn.off { opacity:.35; }
.word-cloud { display:flex; flex-wrap:wrap; gap:5px; margin-top:12px; max-width:340px; }
.word-chip { display:inline-block; border-radius:10px; padding:2px 9px;
    font-size:11px; color:#1E1E28; font-weight:600; }

/* ── Consistency timeline ─────────────────────────────────────────────── */
.timeline-wrapper { overflow-x:auto; }
.timeline-canvas-area { min-width:400px; }

/* ── Note cards (All Notes section) ──────────────────────────────────── */
.note-card { background:#22222A; border:1px solid #3A3A48; border-radius:6px;
    padding:10px 12px; margin-bottom:8px; cursor:pointer;
    transition:background .12s, border-color .12s; }
.note-card:hover { background:#2A2A35; border-color:#5A5A6A; }
.note-card-header { display:flex; align-items:center; gap:6px; margin-bottom:4px; }
.note-dot { width:8px; height:8px; border-radius:50%; flex-shrink:0; }
.note-meta { font-size:11px; color:#9E9AA0; flex:1; }
.note-crumb { font-size:10px; color:#5A5A6A; margin-top:3px; }
.note-nav-hint { font-size:10px; color:#4A4A5E; margin-top:4px; font-style:italic; }
";

            const string appJs = @"
var _data = {};
var EMOTION_COLORS = {
    joy:'#61C88A', sadness:'#61AFEF', anger:'#E06C75',
    fear:'#D19A66', surprise:'#E5C07B', disgust:'#98C379', neutral:'#7A7A8A'
};
var SEVERITY_COLORS = { info:'#61AFEF', warning:'#E5C07B', issue:'#E06C75' };
var RADAR_AXES = ['Sentence\nLength','Vocab\nRichness','Formality','Dialogue','Adverb\nDensity','Passive\nVoice','Clause\nDepth','Complexity'];
var _radarChart = null;
var _timelineChart = null;
var _charVisible = {};

function init(data) {
    if (typeof data === 'string') { try { data = JSON.parse(data); } catch(e) { data = {}; } }
    _data = data;
    renderOverview();
    renderTriage();
    renderPacingHeatmap();
    renderStyleChart();
    renderEmotions();
    renderCharFingerprint();
    renderDialogueFingerprint();
    renderNotes();
}

/* ── Overview ──────────────────────────────────────────────────────────── */
function renderOverview() {
    var el = document.getElementById('overview');
    if (!_data.scenes || _data.scenes.length === 0) {
        el.innerHTML = '<div class=""empty-state"">No analysis data available.</div>';
        return;
    }
    var totalWords = _data.scenes.reduce(function(s,sc){return s+sc.wordCount;},0);
    var totalSentences = _data.scenes.reduce(function(s,sc){return s+sc.sentenceCount;},0);
    var avgSL = totalSentences > 0 ? (totalWords/totalSentences).toFixed(1) : '0';
    var noteCount = _data.notes ? _data.notes.length : 0;
    var chapters = [...new Set(_data.scenes.map(function(s){return s.chapter;}))];
    el.innerHTML =
        '<div class=""stat-card""><div class=""stat-value"">'+totalWords.toLocaleString()+'</div><div class=""stat-label"">Total Words</div></div>'+
        '<div class=""stat-card""><div class=""stat-value"">'+_data.scenes.length+'</div><div class=""stat-label"">Scenes Analyzed</div></div>'+
        '<div class=""stat-card""><div class=""stat-value"">'+chapters.length+'</div><div class=""stat-label"">Chapters</div></div>'+
        '<div class=""stat-card""><div class=""stat-value"">'+avgSL+'</div><div class=""stat-label"">Avg Sentence Length</div></div>'+
        '<div class=""stat-card""><div class=""stat-value"">'+noteCount+'</div><div class=""stat-label"">Notes Generated</div></div>';
}

/* ── Triage panel ──────────────────────────────────────────────────────── */
function renderTriage() {
    var el = document.getElementById('triage');
    if (!_data.notes || _data.notes.length === 0) {
        el.innerHTML = '<div class=""empty-state"">No notes to triage.</div>';
        return;
    }
    var critical = _data.notes.filter(function(n){return n.severity==='issue';});
    var polish   = _data.notes.filter(function(n){return n.severity==='warning';});
    var consider = _data.notes.filter(function(n){return n.severity==='info';});

    function makeCol(title, color, notes) {
        var html = '<div class=""triage-col""><div class=""triage-heading"">' +
            '<div class=""triage-dot"" style=""background:'+color+'""></div>' +
            '<span class=""triage-title"" style=""color:'+color+'"">'+title+'</span>' +
            '<span class=""triage-count"">'+notes.length+'</span></div>';
        if (notes.length === 0) {
            html += '<div class=""empty-state"" style=""padding:12px 0"">None</div>';
        } else {
            notes.forEach(function(n) {
                var crumb = [n.chapter, n.scene].filter(Boolean).join(' \u203a ');
                var navAttr = n.sceneId
                    ? ' onclick=""navigateToNote(\'' + escAttr(n.sceneId) + '\',' + (n.sentenceIndex >= 0 ? n.sentenceIndex : -1) + ')"" title=""Jump to this sentence in the editor""'
                    : '';
                html += '<div class=""triage-item"" style=""border-left-color:'+color+'"" ' + navAttr + '>'+
                    '<div class=""triage-cat"">'+escHtml(n.category)+'</div>'+
                    '<div class=""triage-msg"">'+escHtml(n.message)+'</div>'+
                    (crumb ? '<div class=""triage-crumb"">'+escHtml(crumb)+'</div>' : '')+
                    '</div>';
            });
        }
        html += '</div>';
        return html;
    }

    el.innerHTML = '<div class=""triage-grid"">' +
        makeCol('Critical', '#E06C75', critical) +
        makeCol('Polish',   '#E5C07B', polish) +
        makeCol('Consider', '#61AFEF', consider) +
        '</div>';
}

/* ── Pacing heatmap ────────────────────────────────────────────────────── */
function renderPacingHeatmap() {
    var el = document.getElementById('pacing');
    if (!_data.scenes || _data.scenes.length === 0) return;
    var maxWc = Math.max.apply(null, _data.scenes.map(function(s){return s.wordCount;}));
    var html = '<div class=""heatmap"">';
    _data.scenes.forEach(function(sc) {
        var ratio = maxWc > 0 ? sc.wordCount / maxWc : 0;
        var h = Math.max(8, Math.round(ratio * 80));
        var r = Math.round(100 + ratio * 68);
        var g = Math.round(160 - ratio * 60);
        var b = Math.round(200 + ratio * 1);
        var bg = 'rgb('+r+','+g+','+b+')';
        html += '<div class=""hm-cell"" style=""height:'+h+'px;background:'+bg+'"" '+
            'title=""'+escHtml(sc.title)+' ('+escHtml(sc.chapter)+')\n'+sc.wordCount+' words""></div>';
    });
    html += '</div><div style=""display:flex;gap:2px;margin-top:4px;"">';
    _data.scenes.forEach(function(sc) {
        html += '<div class=""hm-label"" style=""flex:1;min-width:28px;"">'+escHtml(sc.title.substring(0,8))+'</div>';
    });
    html += '</div>';
    el.innerHTML = html;
}

/* ── Style bar chart ───────────────────────────────────────────────────── */
function renderStyleChart() {
    var el = document.getElementById('style');
    if (!_data.scenes || _data.scenes.length === 0) return;
    var maxASL = Math.max.apply(null, _data.scenes.map(function(s){return s.avgSentenceLength;}));
    if (maxASL <= 0) maxASL = 1;
    var html = '<div class=""bar-chart"">';
    _data.scenes.forEach(function(sc) {
        var pct = Math.round((sc.avgSentenceLength / maxASL) * 100);
        var dr = Math.round(sc.dialogueRatio * 100);
        html += '<div class=""bar-col"">' +
            '<div class=""bar"" style=""height:'+pct+'%;background:#A89EC9;"" ' +
            'title=""'+escHtml(sc.title)+'\nAvg sentence: '+sc.avgSentenceLength+' words\nDialogue: '+dr+'%\nContractions: '+Math.round(sc.contractionRate*100)+'%""></div>' +
            '<div class=""bar-label"">'+escHtml(sc.title.substring(0,6))+'</div></div>';
    });
    html += '</div>';
    el.innerHTML = html;
}

/* ── Emotion distribution ──────────────────────────────────────────────── */
function renderEmotions() {
    var el = document.getElementById('emotions');
    if (!_data.scenes) return;
    var hasEmotions = _data.scenes.some(function(s){return s.emotions !== null;});
    if (!hasEmotions) {
        el.innerHTML = '<div class=""empty-state"">No emotion data — download models and re-run analysis for emotion classification.</div>';
        return;
    }
    var html = '';
    _data.scenes.forEach(function(sc) {
        if (!sc.emotions) return;
        html += '<div style=""display:flex;align-items:center;gap:8px;margin-bottom:4px;"">';
        html += '<div style=""width:80px;font-size:11px;color:#9E9AA0;text-align:right;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;"" title=""'+escHtml(sc.title)+' ('+escHtml(sc.chapter)+')"">'+escHtml(sc.title)+'</div>';
        html += '<div class=""emotion-row"" style=""flex:1;"">';
        for (var key in sc.emotions) {
            var pct = Math.round(sc.emotions[key] * 100);
            if (pct < 2) continue;
            var color = EMOTION_COLORS[key] || '#7A7A8A';
            html += '<div class=""emotion-seg"" style=""width:'+pct+'%;background:'+color+'"" title=""'+key+': '+pct+'%""></div>';
        }
        html += '</div></div>';
    });
    html += '<div class=""emotion-legend"">';
    for (var key in EMOTION_COLORS) {
        html += '<div class=""legend-item""><div class=""legend-dot"" style=""background:'+EMOTION_COLORS[key]+'""></div>'+
            key.charAt(0).toUpperCase()+key.slice(1)+'</div>';
    }
    html += '</div>';
    el.innerHTML = html;
}

/* ── Character voice fingerprint ───────────────────────────────────────── */
function renderCharFingerprint() {
    var el = document.getElementById('charFingerprint');
    var profiles = _data.characterProfiles;
    if (!profiles || profiles.length === 0) {
        el.innerHTML = '<div class=""empty-state"">No character profiles — assign viewpoint characters to scenes and re-run analysis.</div>';
        return;
    }

    // Initialize visibility
    profiles.forEach(function(p){ _charVisible[p.id] = true; });

    // Build toggle buttons
    var toggleHtml = '<div class=""char-toggles"" id=""charToggles"">';
    profiles.forEach(function(p) {
        toggleHtml += '<button class=""char-toggle-btn"" id=""btn_'+p.id+'"" '+
            'style=""border-color:'+p.color+';color:'+p.color+'"" '+
            'onclick=""toggleChar(\''+p.id+'\')"">' + escHtml(p.name) + '</button>';
    });
    toggleHtml += '</div>';

    el.innerHTML = toggleHtml +
        '<div class=""char-panel"">' +
        '<div class=""radar-wrapper""><canvas id=""radarChart"" width=""340"" height=""340""></canvas></div>' +
        '<div style=""flex:1;min-width:200px;"">' +
        '<div id=""wordClouds""></div>' +
        '</div></div>' +
        '<h2 style=""margin-top:20px"">Voice Consistency Timeline</h2>' +
        '<div class=""chart-area timeline-wrapper""><div class=""timeline-canvas-area"">' +
        '<canvas id=""timelineChart"" height=""160""></canvas>' +
        '</div></div>';

    drawRadar(profiles);
    renderWordClouds(profiles);
    drawTimeline(profiles);
}

function toggleChar(id) {
    _charVisible[id] = !_charVisible[id];
    var btn = document.getElementById('btn_'+id);
    if (btn) btn.classList.toggle('off', !_charVisible[id]);
    var profiles = _data.characterProfiles.filter(function(p){ return _charVisible[p.id]; });
    if (_radarChart) { _radarChart.destroy(); _radarChart = null; }
    if (_timelineChart) { _timelineChart.destroy(); _timelineChart = null; }
    drawRadar(profiles);
    drawTimeline(profiles);
}

function normalizeProfiles(profiles) {
    // Normalise all 8 axes to 0-1 across the set of profiles
    var keys = ['avgSentenceLength','vocabRichness','contractionRate','dialogueRatio',
                'adverbDensity','passiveVoiceRate','clauseComplexity','readabilityScore'];
    var mins = {}, maxs = {};
    keys.forEach(function(k){ mins[k]=Infinity; maxs[k]=-Infinity; });
    profiles.forEach(function(p) {
        keys.forEach(function(k) {
            if (p[k] < mins[k]) mins[k] = p[k];
            if (p[k] > maxs[k]) maxs[k] = p[k];
        });
    });
    return profiles.map(function(p) {
        var norm = {};
        keys.forEach(function(k) {
            var range = maxs[k] - mins[k];
            norm[k] = range > 0 ? (p[k] - mins[k]) / range : 0.5;
        });
        // Formality = inverse contraction rate; Complexity = inverse readability
        norm.contractionRate = 1 - norm.contractionRate;
        norm.readabilityScore = 1 - norm.readabilityScore;
        return Object.assign({}, p, norm);
    });
}

function drawRadar(profiles) {
    var ctx = document.getElementById('radarChart');
    if (!ctx || !profiles || profiles.length === 0) return;
    var normalised = normalizeProfiles(profiles);
    var labels = ['Sentence Length','Vocab Richness','Formality','Dialogue','Adverb Density','Passive Voice','Clause Depth','Complexity'];
    var datasets = normalised.map(function(p) {
        var hex = p.color;
        return {
            label: p.name,
            data: [p.avgSentenceLength, p.vocabRichness, p.contractionRate, p.dialogueRatio,
                   p.adverbDensity, p.passiveVoiceRate, p.clauseComplexity, p.readabilityScore],
            borderColor: hex,
            backgroundColor: hex + '22',
            pointBackgroundColor: hex,
            pointRadius: 3,
            borderWidth: 2
        };
    });
    _radarChart = new Chart(ctx, {
        type: 'radar',
        data: { labels: labels, datasets: datasets },
        options: {
            responsive: false,
            plugins: { legend: { labels: { color:'#E8E4DC', font:{size:11} } } },
            scales: {
                r: {
                    min: 0, max: 1,
                    ticks: { display:false },
                    grid: { color:'#3A3A48' },
                    angleLines: { color:'#3A3A48' },
                    pointLabels: { color:'#9E9AA0', font:{size:10} }
                }
            }
        }
    });
}

function renderWordClouds(profiles) {
    var el = document.getElementById('wordClouds');
    if (!el) return;
    var html = '';
    profiles.forEach(function(p) {
        if (!p.distinctiveWords || p.distinctiveWords.length === 0) return;
        html += '<div style=""margin-bottom:12px;"">' +
            '<div style=""font-size:11px;color:'+p.color+';font-weight:600;margin-bottom:5px;"">'+escHtml(p.name)+'</div>' +
            '<div class=""word-cloud"">';
        var maxW = Math.max.apply(null, p.distinctiveWords.map(function(w){return w.weight;}));
        if (maxW <= 0) maxW = 1;
        p.distinctiveWords.forEach(function(w) {
            var ratio = w.weight / maxW;
            var size = Math.round(10 + ratio * 6);
            var alpha = Math.round(60 + ratio * 195).toString(16).padStart(2,'0');
            html += '<span class=""word-chip"" style=""background:'+p.color+alpha+';font-size:'+size+'px;"">'+
                escHtml(w.word)+'</span>';
        });
        html += '</div></div>';
    });
    el.innerHTML = html || '<div class=""empty-state"" style=""padding:8px 0"">No distinctive words computed.</div>';
}

function drawTimeline(profiles) {
    var canvas = document.getElementById('timelineChart');
    if (!canvas) return;
    var validProfiles = profiles.filter(function(p){ return p.sceneSnapshots && p.sceneSnapshots.length > 1; });
    if (validProfiles.length === 0) {
        canvas.parentElement.innerHTML = '<div class=""empty-state"">Need at least 2 scenes per character for a timeline.</div>';
        return;
    }

    // Use readabilityScore as the primary timeline metric (most human-readable)
    var maxSnaps = Math.max.apply(null, validProfiles.map(function(p){return p.sceneSnapshots.length;}));
    var labels = [];
    for (var i = 0; i < maxSnaps; i++) labels.push('Scene '+(i+1));

    var datasets = validProfiles.map(function(p) {
        var values = p.sceneSnapshots.map(function(s){ return Math.round(s.readabilityScore); });
        return {
            label: p.name + ' (Readability)',
            data: values,
            borderColor: p.color,
            backgroundColor: 'transparent',
            pointBackgroundColor: p.color,
            tension: 0.35,
            borderWidth: 2,
            pointRadius: 4
        };
    });

    // Set canvas width based on scene count
    canvas.width = Math.max(400, maxSnaps * 60);

    _timelineChart = new Chart(canvas, {
        type: 'line',
        data: { labels: labels, datasets: datasets },
        options: {
            responsive: false,
            animation: false,
            plugins: { legend: { labels: { color:'#E8E4DC', font:{size:11} } },
                       tooltip: { callbacks: { label: function(ctx) {
                           var snap = validProfiles[ctx.datasetIndex]?.sceneSnapshots[ctx.dataIndex];
                           if (!snap) return ctx.dataset.label+': '+ctx.parsed.y;
                           return ctx.dataset.label+': '+ctx.parsed.y+' ('+snap.scene+')';
                       }}}},
            scales: {
                x: { ticks: { color:'#9E9AA0', font:{size:10} }, grid: { color:'#3A3A48' } },
                y: { min:0, max:100,
                     title: { display:true, text:'Readability (Flesch)', color:'#9E9AA0', font:{size:10} },
                     ticks: { color:'#9E9AA0', font:{size:10} }, grid: { color:'#3A3A48' } }
            }
        }
    });
}

/* ── Dialogue voice fingerprint ───────────────────────────────────────── */
var _dialogueRadarChart = null;
var _dialogueCharVisible = {};
var DIALOGUE_RADAR_AXES = ['Sentence\nLength','Vocab\nRichness','Formality','Exclamation','Adverb\nDensity','Passive\nVoice','Clause\nDepth','Complexity'];

function renderDialogueFingerprint() {
    var el = document.getElementById('dialogueFingerprint');
    var profiles = _data.dialogueProfiles;
    if (!profiles || profiles.length === 0) {
        el.innerHTML = '<div class=""empty-state"">No dialogue profiles \u2014 ensure characters are defined and dialogue uses speech tags (e.g. \u201CHello,\u201D Sarah said).</div>';
        return;
    }

    profiles.forEach(function(p){ _dialogueCharVisible[p.id] = true; });

    var toggleHtml = '<div class=""char-toggles"" id=""dialogueCharToggles"">';
    profiles.forEach(function(p) {
        toggleHtml += '<button class=""char-toggle-btn"" id=""dbtn_'+p.id+'"" '+
            'style=""border-color:'+p.color+';color:'+p.color+'"" '+
            'onclick=""toggleDialogueChar(\''+p.id+'\')"">' + escHtml(p.name) +
            ' <span style=""font-size:9px;opacity:.6"">('+p.dialogueLineCount+' lines)</span></button>';
    });
    toggleHtml += '</div>';

    el.innerHTML = toggleHtml +
        '<div class=""char-panel"">' +
        '<div class=""radar-wrapper""><canvas id=""dialogueRadarChart"" width=""340"" height=""340""></canvas></div>' +
        '<div style=""flex:1;min-width:200px;"">' +
        '<div id=""dialogueWordClouds""></div>' +
        '<div id=""similarityWarnings"" style=""margin-top:12px;""></div>' +
        '</div></div>';

    drawDialogueRadar(profiles);
    renderDialogueWordClouds(profiles);
    renderSimilarityWarnings();
}

function toggleDialogueChar(id) {
    _dialogueCharVisible[id] = !_dialogueCharVisible[id];
    var btn = document.getElementById('dbtn_'+id);
    if (btn) btn.classList.toggle('off', !_dialogueCharVisible[id]);
    var profiles = _data.dialogueProfiles.filter(function(p){ return _dialogueCharVisible[p.id]; });
    if (_dialogueRadarChart) { _dialogueRadarChart.destroy(); _dialogueRadarChart = null; }
    drawDialogueRadar(profiles);
}

function normalizeDialogueProfiles(profiles, allProfiles) {
    var keys = ['avgSentenceLength','vocabRichness','contractionRate','exclamationRate',
                'adverbDensity','passiveVoiceRate','clauseComplexity','readabilityScore'];
    var mins = {}, maxs = {};
    keys.forEach(function(k){ mins[k]=Infinity; maxs[k]=-Infinity; });
    allProfiles.forEach(function(p) {
        keys.forEach(function(k) {
            if (p[k] < mins[k]) mins[k] = p[k];
            if (p[k] > maxs[k]) maxs[k] = p[k];
        });
    });
    return profiles.map(function(p) {
        var norm = {};
        keys.forEach(function(k) {
            var range = maxs[k] - mins[k];
            norm[k] = range > 0 ? (p[k] - mins[k]) / range : 0.5;
        });
        norm.contractionRate = 1 - norm.contractionRate;
        norm.readabilityScore = 1 - norm.readabilityScore;
        return Object.assign({}, p, norm);
    });
}

function drawDialogueRadar(profiles) {
    var ctx = document.getElementById('dialogueRadarChart');
    if (!ctx || !profiles || profiles.length === 0) return;
    var normalised = normalizeDialogueProfiles(profiles, _data.dialogueProfiles);
    var labels = ['Sentence Length','Vocab Richness','Formality','Exclamation','Adverb Density','Passive Voice','Clause Depth','Complexity'];
    var datasets = normalised.map(function(p) {
        return {
            label: p.name,
            data: [p.avgSentenceLength, p.vocabRichness, p.contractionRate, p.exclamationRate,
                   p.adverbDensity, p.passiveVoiceRate, p.clauseComplexity, p.readabilityScore],
            borderColor: p.color,
            backgroundColor: p.color + '22',
            pointBackgroundColor: p.color,
            pointRadius: 3,
            borderWidth: 2
        };
    });
    _dialogueRadarChart = new Chart(ctx, {
        type: 'radar',
        data: { labels: labels, datasets: datasets },
        options: {
            responsive: false,
            plugins: { legend: { labels: { color:'#E8E4DC', font:{size:11} } } },
            scales: {
                r: {
                    min: 0, max: 1,
                    ticks: { display:false },
                    grid: { color:'#3A3A48' },
                    angleLines: { color:'#3A3A48' },
                    pointLabels: { color:'#9E9AA0', font:{size:10} }
                }
            }
        }
    });
}

function renderDialogueWordClouds(profiles) {
    var el = document.getElementById('dialogueWordClouds');
    if (!el) return;
    var html = '';
    profiles.forEach(function(p) {
        if (!p.distinctiveWords || p.distinctiveWords.length === 0) return;
        html += '<div style=""margin-bottom:12px;"">' +
            '<div style=""font-size:11px;color:'+p.color+';font-weight:600;margin-bottom:5px;"">'+escHtml(p.name)+' \u2014 Dialogue</div>' +
            '<div class=""word-cloud"">';
        var maxW = Math.max.apply(null, p.distinctiveWords.map(function(w){return w.weight;}));
        if (maxW <= 0) maxW = 1;
        p.distinctiveWords.forEach(function(w) {
            var ratio = w.weight / maxW;
            var size = Math.round(10 + ratio * 6);
            var alpha = Math.round(60 + ratio * 195).toString(16).padStart(2,'0');
            html += '<span class=""word-chip"" style=""background:'+p.color+alpha+';font-size:'+size+'px;"">'+
                escHtml(w.word)+'</span>';
        });
        html += '</div></div>';
    });
    el.innerHTML = html || '<div class=""empty-state"" style=""padding:8px 0"">No distinctive dialogue words computed.</div>';
}

function renderSimilarityWarnings() {
    var el = document.getElementById('similarityWarnings');
    if (!el) return;
    if (!_data.notes) { el.innerHTML = ''; return; }
    var warnings = _data.notes.filter(function(n){
        return n.category === 'Developmental Editor' && n.message && n.message.indexOf('sound very similar in dialogue') >= 0;
    });
    if (warnings.length === 0) { el.innerHTML = ''; return; }
    var html = '<div style=""font-size:11px;font-weight:600;color:#E5C07B;margin-bottom:6px;"">\u26A0 Voice Similarity Warnings</div>';
    warnings.forEach(function(n) {
        html += '<div style=""background:#2A2A35;border:1px solid #E5C07B44;border-radius:4px;padding:8px 10px;margin-bottom:6px;font-size:11px;color:#C8C4BC;line-height:1.45;"">'+
            escHtml(n.message)+'</div>';
    });
    el.innerHTML = html;
}

/* ── All notes (flat list after triage) ────────────────────────────────── */
function renderNotes() {
    var el = document.getElementById('notes');
    if (!_data.notes || _data.notes.length === 0) {
        el.innerHTML = '<div class=""empty-state"">No notes generated.</div>';
        return;
    }
    var html = '';
    _data.notes.forEach(function(n) {
        var color = SEVERITY_COLORS[n.severity] || '#61AFEF';
        var crumb = [n.chapter, n.scene].filter(Boolean).join(' \u203a ');
        var hasNav = !!n.sceneId;
        var navAttr = hasNav
            ? ' onclick=""navigateToNote(\'' + escAttr(n.sceneId) + '\',' + (n.sentenceIndex >= 0 ? n.sentenceIndex : -1) + ')"" title=""Jump to this sentence in the editor""'
            : '';
        var hint = hasNav
            ? (n.sentenceIndex >= 0 ? 'Click to jump to sentence' : 'Click to open scene')
            : '';
        html += '<div class=""note-card""' + navAttr + '>' +
            '<div class=""note-card-header"">' +
            '<div class=""note-dot"" style=""background:'+color+'""></div>' +
            '<span class=""note-meta"">'+escHtml(n.category)+
            (crumb ? ' \u00b7 '+escHtml(crumb) : '')+'</span></div>' +
            '<div style=""font-size:12px;color:#E8E4DC;line-height:1.5;"">'+escHtml(n.message)+'</div>'+
            (hint ? '<div class=""note-nav-hint"">'+escHtml(hint)+'</div>' : '')+
            '</div>';
    });
    el.innerHTML = html;
}

function escHtml(s) {
    if (!s) return '';
    return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/\n/g,'<br>');
}

function escAttr(s) {
    if (!s) return '';
    return String(s).replace(/\\/g,'\\\\').replace(/'/g,'\\'+'\'');
}

function navigateToNote(sceneId, sentenceIndex) {
    if (!sceneId) return;
    var url = 'alphawriter://noteNavigate?sceneId=' + encodeURIComponent(sceneId);
    if (sentenceIndex >= 0) url += '&sentenceIndex=' + sentenceIndex;
    window.location.href = url;
}
";

            // Chart.js is inlined (caller loaded it from Resources/Raw via FileSystem.OpenAppPackageFileAsync)
            string chartJsScript = string.IsNullOrEmpty(chartJs)
                ? string.Empty
                : "<script>" + chartJs + "</" + "script>";

            const string bodyHtml = @"
<div class=""container"">
    <h2>Overview</h2>
    <div id=""overview"" class=""stats-grid""></div>

    <h2>Editorial Triage</h2>
    <div id=""triage""></div>

    <h2>Pacing Heatmap</h2>
    <div id=""pacing"" class=""chart-area""></div>

    <h2>Style Consistency</h2>
    <div id=""style"" class=""chart-area""></div>

    <h2>Emotion Distribution</h2>
    <div id=""emotions"" class=""chart-area""></div>

    <h2>Character Voice Fingerprint</h2>
    <div id=""charFingerprint"" class=""chart-area""></div>

    <h2>Dialogue Voice Fingerprint</h2>
    <div id=""dialogueFingerprint"" class=""chart-area""></div>

    <h2>All Notes</h2>
    <div id=""notes""></div>
</div>
";

            return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>"
                 + "<style>" + css + "</style>"
                 + chartJsScript
                 + "</head><body>"
                 + bodyHtml
                 + "<script>" + appJs
                 + "init(" + dataJson + ");"
                 + "</" + "script></body></html>";
        }

        // ── JS string decoding ────────────────────────────────────────────────
        // WebView2 on Windows returns EvaluateJavaScriptAsync results as a JSON
        // string WITH outer quotes on some MAUI versions, and WITHOUT on others.
        // When the outer quotes are absent, JSON deserialization fails and the
        // raw string may still contain \uXXXX escape sequences (e.g. \u003C for
        // '<'). We decode those sequences so the stored HTML always has real
        // angle-bracket characters.
        private static string DecodeJsString(string? raw)
        {
            if (raw is null) return string.Empty;
            try { return System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? string.Empty; }
            catch { }
            // Decode \uXXXX sequences left by WebView2's unquoted JSON encoding.
            return System.Text.RegularExpressions.Regex.Replace(
                raw,
                @"\\u([0-9a-fA-F]{4})",
                m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        }

        // ── Global drag-glyph suppression (Windows) ──────────────────────────
        // Called once from OnAppearing (page is guaranteed to be in the visual tree).
        //
        // Two-pronged:
        //  1. Attach OnNativeDragOver to the WinUI window's content root with
        //     handledEventsToo:true — fires for every drag over any normal WinUI
        //     element, even those already handled by child DropGestureRecognizers.
        //  2. Set AllowDrop=false on the native WebView2 — WebView2 runs in its own
        //     Win32 HWND and doesn't propagate WinUI drag events up the tree, but it
        //     signals "Copy" to the OS by default; disabling AllowDrop stops that.
        private void AttachGlobalDragSuppressor()
        {
            if (_dragSuppressorAttached) return;
            _dragSuppressorAttached = true;
#if WINDOWS
            // 1. Window content root — covers all non-WebView areas
            if (Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow &&
                nativeWindow.Content is Microsoft.UI.Xaml.FrameworkElement windowContent)
            {
                windowContent.AllowDrop = true;
                windowContent.AddHandler(
                    Microsoft.UI.Xaml.UIElement.DragOverEvent,
                    new Microsoft.UI.Xaml.DragEventHandler(OnNativeDragOver),
                    handledEventsToo: true);
            }

            // 2. WebView2 — prevent it accepting drops so the OS shows no glyph over it
            if (EditorWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 wv2)
                wv2.AllowDrop = false;
#endif
        }

#if WINDOWS
        private static void OnNativeDragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            if (e.DragUIOverride is { } ui)
            {
                ui.IsGlyphVisible   = false;
                ui.IsCaptionVisible = false;
                ui.IsContentVisible = false;
            }
        }
#endif

        // ── Resizable splitter ──────────────────────────────────────────────────

        private double _previousPanY;

        private void OnSplitterPanUpdated(object? sender, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _previousPanY = 0;
                    break;

                case GestureStatus.Running:
                    var delta = e.TotalY - _previousPanY;
                    _previousPanY = e.TotalY;
                    AdjustSplitRows(delta);
                    break;
            }
        }

        private void AdjustSplitRows(double deltaY)
        {
            var grid = EditorScenesGrid;
            var totalHeight = grid.Height;
            if (totalHeight <= 0) return;

            var editorRow = grid.RowDefinitions[0];
            var scenesRow = grid.RowDefinitions[2];

            var totalStar = editorRow.Height.Value + scenesRow.Height.Value;
            var usableHeight = totalHeight - 6.0;

            var editorPx = (editorRow.Height.Value / totalStar) * usableHeight + deltaY;
            var scenesPx = (scenesRow.Height.Value / totalStar) * usableHeight - deltaY;

            const double minEditor = 120;
            const double minScenes = 80;
            editorPx = Math.Max(editorPx, minEditor);
            scenesPx = Math.Max(scenesPx, minScenes);

            var newTotal = editorPx + scenesPx;
            editorRow.Height = new GridLength(editorPx / newTotal, GridUnitType.Star);
            scenesRow.Height = new GridLength(scenesPx / newTotal, GridUnitType.Star);
        }

        // ── Drag-and-drop reordering ──────────────────────────────────────────

        private Chapter? _draggingChapter;
        private Scene?   _draggingScene;

        private bool _chapterDropAfter;
        private bool _sceneDropAfter;

        // Returns true when the drag cursor is in the lower half of the hovered item.
        private static bool IsCursorInBottomHalf(object? sender, DragEventArgs e)
        {
#if WINDOWS
            if (e.PlatformArgs is PlatformDragEventArgs pd &&
                pd.DragEventArgs is { } nativeArgs &&
                sender is VisualElement ve &&
                ve.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement nativeEl)
            {
                var pt = nativeArgs.GetPosition(nativeEl);
                return pt.Y > nativeEl.ActualHeight / 2.0;
            }
#endif
            return false;
        }

        // Row 0 = top drop indicator, Row 2 = bottom drop indicator.
        // atBottom=true highlights the bottom edge.
        private static void ShowDropIndicator(object? sender, bool atBottom = false)
        {
            if (sender is not Grid g || g.Children.Count < 2) return;
            var top = g.Children[0]  as BoxView;
            var bot = g.Children[^1] as BoxView;
            if (top != null) top.IsVisible = false;
            if (bot != null) bot.IsVisible = false;
            if (atBottom && bot != null) bot.IsVisible = true;
            else if (!atBottom && top != null) top.IsVisible = true;
        }

        private static void HideDropIndicator(object? sender)
        {
            if (sender is not Grid g) return;
            if (g.Children.Count > 0 && g.Children[0] is BoxView top)   top.IsVisible = false;
            if (g.Children.Count > 1 && g.Children[^ 1] is BoxView bot)  bot.IsVisible = false;
        }

        // Suppress the platform "Copy" callout/glyph that appears beside the cursor.
        private static void SuppressDragCallout(DragEventArgs e)
        {
#if WINDOWS
            if (e.PlatformArgs is PlatformDragEventArgs pd &&
                pd.DragEventArgs?.DragUIOverride is { } ui)
            {
                ui.IsGlyphVisible   = false;
                ui.IsCaptionVisible = false;
                ui.IsContentVisible = false;
            }
#endif
        }

        // Chapters
        private void OnChapterDragStarting(object? sender, DragStartingEventArgs e)
        {
            if (sender is Element el && el.BindingContext is Chapter chapter)
                _draggingChapter = chapter;
        }

        private void OnChapterDragCompleted(object? sender, DropCompletedEventArgs e)
        { _draggingChapter = null; _chapterDropAfter = false; }

        private void OnChapterDragOver(object? sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            if (sender is Element el && el.BindingContext is Chapter target)
            {
                var chapters = _viewModel.SelectedBook?.Chapters;
                var isLast = chapters is { Count: > 0 } && chapters[^1] == target;
                _chapterDropAfter = isLast && IsCursorInBottomHalf(sender, e);
            }
            ShowDropIndicator(sender, atBottom: _chapterDropAfter);
            SuppressDragCallout(e);
        }

        private void OnChapterDragLeave(object? sender, DragEventArgs e)
            => HideDropIndicator(sender);

        private void OnChapterDrop(object? sender, DropEventArgs e)
        {
            HideDropIndicator(sender);
            if (_draggingChapter is null) return;
            if (sender is Element el && el.BindingContext is Chapter target && target != _draggingChapter)
            {
                var chapters = _viewModel.SelectedBook?.Chapters;
                if (chapters is not null)
                {
                    var idx = chapters.IndexOf(target);
                    if (_chapterDropAfter) idx++;
                    _viewModel.MoveChapter(_draggingChapter, idx);
                }
            }
            _draggingChapter = null;
            _chapterDropAfter = false;
        }

        // Scenes
        private void OnSceneDragStarting(object? sender, DragStartingEventArgs e)
        {
            if (sender is Element el && el.BindingContext is Scene scene)
                _draggingScene = scene;
        }

        private void OnSceneDragCompleted(object? sender, DropCompletedEventArgs e)
        { _draggingScene = null; _sceneDropAfter = false; }

        private void OnSceneDragOver(object? sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            if (sender is Element el && el.BindingContext is Scene target)
            {
                var scenes = _viewModel.SelectedChapter?.Scenes;
                var isLast = scenes is { Count: > 0 } && scenes[^1] == target;
                _sceneDropAfter = isLast && IsCursorInBottomHalf(sender, e);
            }
            ShowDropIndicator(sender, atBottom: _sceneDropAfter);
            SuppressDragCallout(e);
        }

        private void OnSceneDragLeave(object? sender, DragEventArgs e)
            => HideDropIndicator(sender);

        private void OnSceneDrop(object? sender, DropEventArgs e)
        {
            HideDropIndicator(sender);
            if (_draggingScene is null) return;
            if (sender is Element el && el.BindingContext is Scene target && target != _draggingScene)
            {
                var scenes = _viewModel.SelectedChapter?.Scenes;
                if (scenes is not null)
                {
                    var idx = scenes.IndexOf(target);
                    if (_sceneDropAfter) idx++;
                    _viewModel.MoveScene(_draggingScene, idx);
                }
            }
            _draggingScene = null;
            _sceneDropAfter = false;
        }

        private void OnSceneTitleUnfocused(object sender, FocusEventArgs e)
            => _viewModel.SaveSceneTitle();

        private void OnElementFieldUnfocused(object? sender, FocusEventArgs e)
            => _viewModel.SaveElementDetails();

        private void OnSceneMetadataUnfocused(object? sender, FocusEventArgs e)
            => _viewModel.SaveSceneMetadata();

        private void OnBookInfoFieldUnfocused(object? sender, FocusEventArgs e)
            => _viewModel.SaveBookInfo();

        // ── Formatting toolbar ────────────────────────────────────────────────

        private async void OnBoldClicked(object sender, EventArgs e)
            => await EditorWebView.EvaluateJavaScriptAsync("execFormat('bold')");

        private async void OnItalicClicked(object sender, EventArgs e)
            => await EditorWebView.EvaluateJavaScriptAsync("execFormat('italic')");

        private async void OnUnderlineClicked(object sender, EventArgs e)
            => await EditorWebView.EvaluateJavaScriptAsync("execFormat('underline')");

        // ── Prose heat map overlay ────────────────────────────────────────────

        private async void OnHeatMapClicked(object sender, EventArgs e)
        {
            if (!_editorReady) return;

            _heatMapActive = !_heatMapActive;

            // Update button visual state
            if (HeatMapButton is not null)
            {
                HeatMapButton.Opacity = _heatMapActive ? 1.0 : 0.5;
                HeatMapButton.BackgroundColor = _heatMapActive
                    ? Color.FromArgb("#3A3A5A")
                    : Colors.Transparent;
            }

            if (_heatMapActive)
            {
                var heatJson = _viewModel.BuildHeatMapJson();
                if (!string.IsNullOrEmpty(heatJson))
                    await EditorWebView.EvaluateJavaScriptAsync($"applyHeatMap({heatJson})");
            }
            else
            {
                await EditorWebView.EvaluateJavaScriptAsync("clearHeatMap()");
            }
        }

        /// <summary>
        /// Called when a new scene is loaded into the editor — reapplies the heat map
        /// if it was active so overlays appear on the new content.
        /// </summary>
        private async Task ReapplyHeatMapIfActiveAsync()
        {
            if (!_heatMapActive || !_editorReady) return;
            var heatJson = _viewModel.BuildHeatMapJson();
            if (!string.IsNullOrEmpty(heatJson))
                await EditorWebView.EvaluateJavaScriptAsync($"applyHeatMap({heatJson})");
        }

        // ── Note navigation from report panel ─────────────────────────────────

        private async void OnReportNavigating(object? sender, WebNavigatingEventArgs e)
        {
            if (!e.Url.StartsWith("alphawriter://noteNavigate", StringComparison.OrdinalIgnoreCase))
                return;

            e.Cancel = true;

            var sceneId     = ExtractQueryParam(e.Url, "sceneId");
            var sentIdxStr  = ExtractQueryParam(e.Url, "sentenceIndex");
            int? sentenceIdx = int.TryParse(sentIdxStr, out var si) && si >= 0 ? si : null;

            if (sceneId is null) return;
            await NavigateToNoteAsync(sceneId, sentenceIdx);
        }

        /// <summary>
        /// Navigates the editor to the scene identified by <paramref name="sceneId"/>,
        /// then scrolls to and flash-highlights the sentence at <paramref name="sentenceIndex"/>.
        /// If the scene is already open, the scroll fires immediately.
        /// If the scene must be switched, <see cref="_pendingScrollSentenceIndex"/> is set
        /// so <see cref="LoadSceneAsync"/> applies the scroll after the content loads.
        /// </summary>
        private async Task NavigateToNoteAsync(string sceneId, int? sentenceIndex)
        {
            if (!_editorReady) return;

            if (_currentEditorScene?.Id == sceneId)
            {
                // Scene already loaded — jump directly
                if (sentenceIndex.HasValue)
                    await EditorWebView.EvaluateJavaScriptAsync($"scrollToSentence({sentenceIndex.Value})");
                return;
            }

            // Find the scene in the book
            var scene = FindSceneById(sceneId);
            if (scene is null) return;

            // Store the pending index before triggering the scene switch so that
            // LoadSceneAsync (which runs on the SelectedSceneChanged path) can consume it.
            _pendingScrollSentenceIndex = sentenceIndex;

            await MainThread.InvokeOnMainThreadAsync(() =>
                _viewModel.SelectedScene = scene);
        }

        private Scene? FindSceneById(string sceneId)
        {
            if (_viewModel.SelectedBook is null) return null;
            foreach (var chapter in _viewModel.SelectedBook.Chapters)
                foreach (var scene in chapter.Scenes)
                    if (scene.Id == sceneId) return scene;
            return null;
        }
    }
}
