using alphaWriter.Models;
using alphaWriter.ViewModels;

namespace alphaWriter.Views
{
    public partial class WriterPage : ContentPage
    {
        private WriterViewModel _viewModel;
        private bool _editorReady;
        private Scene? _currentEditorScene; // The scene currently loaded in the WebView

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

            if (scene is null)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await EditorWebView.EvaluateJavaScriptAsync("setContent('')"));
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[EDITOR] Loading scene '{scene.Title}', Content length={scene.Content?.Length ?? -1}, Content preview='{scene.Content?.Substring(0, Math.Min(scene.Content?.Length ?? 0, 80))}'");
            await LoadSceneAsync(scene);
            _currentEditorScene = scene;
            System.Diagnostics.Debug.WriteLine($"[EDITOR] Scene '{scene.Title}' loaded, _currentEditorScene set");
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

        private async Task LoadReportAsync()
        {
            try
            {
                if (_viewModel.ActiveReportType == "NLP")
                {
                    var dataJson = _viewModel.BuildNlpReportJson();
                    System.Diagnostics.Debug.WriteLine(
                        $"[REPORT] LoadReportAsync NLP: dataJson length={dataJson.Length}");
                    var html = BuildNlpReportHtml(dataJson);
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
        private static string BuildNlpReportHtml(string dataJson)
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
.note-card { background:#22222A; border:1px solid #3A3A48; border-radius:6px;
    padding:10px 12px; margin-bottom:6px; }
.note-header { display:flex; align-items:center; gap:6px; margin-bottom:4px; }
.note-dot { width:8px; height:8px; border-radius:50%; flex-shrink:0; }
.note-cat { font-size:11px; color:#9E9AA0; }
.note-msg { font-size:12px; color:#E8E4DC; line-height:1.5; }
.empty-state { color:#5A5A6A; font-style:italic; text-align:center; padding:30px; }
.tooltip { position:absolute; background:#2A2A35; border:1px solid #4A4A5C;
    color:#E8E4DC; font-size:11px; border-radius:4px; padding:4px 8px;
    pointer-events:none; z-index:10; white-space:nowrap;
    box-shadow:0 4px 12px rgba(0,0,0,.5); display:none; }
.stats-grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(180px,1fr)); gap:8px; margin-bottom:16px; }
.stat-card { background:#22222A; border:1px solid #3A3A48; border-radius:6px; padding:10px 12px; }
.stat-value { font-size:20px; font-weight:700; color:#A89EC9; }
.stat-label { font-size:11px; color:#9E9AA0; margin-top:2px; }
";

            const string appJs = @"
var _data = {};
var EMOTION_COLORS = {
    joy:'#61C88A', sadness:'#61AFEF', anger:'#E06C75',
    fear:'#D19A66', surprise:'#E5C07B', disgust:'#98C379', neutral:'#7A7A8A'
};
var SEVERITY_COLORS = { info:'#61AFEF', warning:'#E5C07B', issue:'#E06C75' };

function init(data) {
    if (typeof data === 'string') { try { data = JSON.parse(data); } catch(e) { data = {}; } }
    _data = data;
    renderOverview();
    renderPacingHeatmap();
    renderStyleChart();
    renderEmotions();
    renderNotes();
}

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

function renderPacingHeatmap() {
    var el = document.getElementById('pacing');
    if (!_data.scenes || _data.scenes.length === 0) return;
    var maxWc = Math.max.apply(null, _data.scenes.map(function(s){return s.wordCount;}));
    var html = '<div class=""heatmap"">';
    _data.scenes.forEach(function(sc) {
        var ratio = maxWc > 0 ? sc.wordCount / maxWc : 0;
        var h = Math.max(8, Math.round(ratio * 80));
        // Color: short=cool(blue) to long=warm(purple)
        var r = Math.round(100 + ratio * 68);
        var g = Math.round(160 - ratio * 60);
        var b = Math.round(200 + ratio * 1);
        var bg = 'rgb('+r+','+g+','+b+')';
        html += '<div class=""hm-cell"" style=""height:'+h+'px;background:'+bg+'"" '+
            'title=""'+sc.title+' ('+sc.chapter+')\n'+sc.wordCount+' words"">' +
            '</div>';
    });
    html += '</div><div style=""display:flex;gap:2px;margin-top:4px;"">';
    _data.scenes.forEach(function(sc) {
        html += '<div class=""hm-label"" style=""flex:1;min-width:28px;"">'+sc.title.substring(0,8)+'</div>';
    });
    html += '</div>';
    el.innerHTML = html;
}

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
            'title=""'+sc.title+'\nAvg sentence: '+sc.avgSentenceLength+' words\nDialogue: '+dr+'%\nContractions: '+Math.round(sc.contractionRate*100)+'%""></div>' +
            '<div class=""bar-label"">'+sc.title.substring(0,6)+'</div></div>';
    });
    html += '</div>';
    el.innerHTML = html;
}

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
        if (!sc.emotions) { return; }
        html += '<div style=""display:flex;align-items:center;gap:8px;margin-bottom:4px;"">';
        html += '<div style=""width:80px;font-size:11px;color:#9E9AA0;text-align:right;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;"" title=""'+sc.title+' ('+sc.chapter+')"">'+sc.title+'</div>';
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

function renderNotes() {
    var el = document.getElementById('notes');
    if (!_data.notes || _data.notes.length === 0) {
        el.innerHTML = '<div class=""empty-state"">No notes generated.</div>';
        return;
    }
    var html = '';
    _data.notes.forEach(function(n) {
        var color = SEVERITY_COLORS[n.severity] || '#61AFEF';
        html += '<div class=""note-card"">' +
            '<div class=""note-header"">' +
            '<div class=""note-dot"" style=""background:'+color+'""></div>' +
            '<span class=""note-cat"">'+n.category+(n.chapter?' / '+n.chapter:'')+
            (n.scene?' / '+n.scene:'')+'</span></div>' +
            '<div class=""note-msg"">'+escHtml(n.message)+'</div></div>';
    });
    el.innerHTML = html;
}

function escHtml(s) {
    return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/\n/g,'<br>');
}
";

            const string bodyHtml = @"
<div class=""container"">
    <h2>Overview</h2>
    <div id=""overview"" class=""stats-grid""></div>

    <h2>Pacing Heatmap</h2>
    <div id=""pacing"" class=""chart-area""></div>

    <h2>Style Consistency</h2>
    <div id=""style"" class=""chart-area""></div>

    <h2>Emotion Distribution</h2>
    <div id=""emotions"" class=""chart-area""></div>

    <h2>Analysis Notes</h2>
    <div id=""notes""></div>
</div>
";

            return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>"
                 + "<style>" + css + "</style></head><body>"
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
    }
}
