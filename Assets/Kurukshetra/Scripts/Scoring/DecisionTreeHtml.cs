using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Kurukshetra.Trainer;

namespace Kurukshetra.Scoring
{
    // The decision tree in the HTML report, drawn like the SCORE dashboard's tree (DecisionTreeView): a light slate panel,
    // blue checkpoint bars joined by connectors, branches with a tone edge, count, share chip and share strip, ALL / < / > to
    // light one contact's path, a click on a bar folds it, and for one contact the details and its decision history.
    // Structure, labels, tones and order come from DecisionTreeData, so the report and the game never disagree.
    // The tree data sits in the page as JSON and a small inline script draws it; works offline, no libraries.
    public static class DecisionTreeHtml
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string Sec(float v) => v < 0f ? "-" : v.ToString("F1", Inv) + " s";

        // JSON string literal, safe inside a <script> block.
        static string J(string s)
        {
            var b = new StringBuilder("\"");
            foreach (char ch in s ?? "")
            {
                switch (ch)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': break;
                    case '\t': b.Append("\\t"); break;
                    case '<': b.Append("\\u003c"); break;
                    case '>': b.Append("\\u003e"); break;
                    case '&': b.Append("\\u0026"); break;
                    default:
                        if (ch < 0x20) b.Append("\\u").Append(((int)ch).ToString("x4"));
                        else b.Append(ch);
                        break;
                }
            }
            return b.Append('"').ToString();
        }

        public static void Append(StringBuilder h, SessionResult s)
        {
            if (s == null || s.contacts == null) return;
            var stages = DecisionTreeData.Build(s);
            var d = new StringBuilder(8192);
            d.Append("{\"s\":[");
            for (int i = 0; i < stages.Count; i++)
            {
                if (i > 0) d.Append(',');
                d.Append("{\"t\":").Append(J(stages[i].title)).Append(",\"n\":[");
                for (int k = 0; k < stages[i].nodes.Count; k++)
                {
                    var n = stages[i].nodes[k];
                    if (k > 0) d.Append(',');
                    d.Append('[').Append(J(n.key)).Append(',').Append(J(n.label)).Append(',').Append(n.tone).Append(']');
                }
                d.Append("]}");
            }
            d.Append("],\"c\":[");
            for (int i = 0; i < s.contacts.Count; i++)
            {
                var c = s.contacts[i];
                if (i > 0) d.Append(',');
                d.Append("{\"id\":").Append(c.id).Append(",\"r\":").Append(c.unresolved ? 0 : 1)
                    .Append(",\"who\":").Append(J(c.truthClass + (c.deceptive ? "  (blend-in)" : "") + (c.group ? "  [group]" : "") + "   = " + (c.scored ? c.total.ToString() : "not scored")))
                    .Append(",\"b\":[");
                for (int st = 0; st < DecisionTreeData.StageCount; st++) { if (st > 0) d.Append(','); d.Append(J(DecisionTreeData.Branch(c, st))); }
                d.Append("],\"f\":[");
                bool first = true;
                foreach (var row in Facts(c)) { if (!first) d.Append(','); first = false; d.Append('[').Append(J(row.Key)).Append(',').Append(J(row.Value)).Append(']'); }
                d.Append("],\"chg\":").Append(J(c.callChanges + c.actionChanges > 0
                    ? $"{c.callChanges} call change{(c.callChanges == 1 ? "" : "s")}, {c.actionChanges} action change{(c.actionChanges == 1 ? "" : "s")}" + (c.changePenalty != 0 ? $", {c.changePenalty} for second-guessing" : "") : ""));
                d.Append(",\"h\":[");
                if (c.history != null) for (int k = 0; k < c.history.Count; k++) { if (k > 0) d.Append(','); d.Append(J(c.history[k])); }
                d.Append("]}");
            }
            d.Append("]}");

            h.Append("<section class=\"dtree\"><div class=\"dt-top\"><h2>Decision tree</h2></div>");
            h.Append("<div class=\"dtx\"><script type=\"application/json\" class=\"dt-data\">").Append(d.ToString()).Append("</script>");
            h.Append("<noscript><p class=\"small\">Turn on scripts in the browser to draw the decision tree.</p></noscript></div></section>");
        }

        // The same details the game shows for one contact, as label / value rows.
        static List<KeyValuePair<string, string>> Facts(ContactResult c)
        {
            var r = new List<KeyValuePair<string, string>>();
            void Add(string k, string v) => r.Add(new KeyValuePair<string, string>(k, v));
            string grid = string.IsNullOrEmpty(c.grid) ? "No decision" : c.grid;
            Add("Truth", c.truthClass + (c.deceptive ? " (hostile flying an own-drone pattern)" : ""));
            if (!c.scored) Add("Not scored", c.notScoredReason ?? "");
            Add("Seen", c.detected ? "after " + Sec(c.detectionSeconds) : "never");
            if (c.n1aSeconds >= 0f || c.n1bSeconds >= 0f) Add("Search / lock", Sec(c.n1aSeconds) + " to come into view, " + Sec(c.n1bSeconds) + " to lock");
            Add("Call", (string.IsNullOrEmpty(c.call) ? "none" : c.call) + (c.callChanges > 0 ? " (first " + c.firstCall + ", changed " + c.callChanges + "x)" : ""));
            if (!string.IsNullOrEmpty(c.aiHint)) Add("AI cue", c.aiHint + " " + c.aiConfidence + " %" + (c.aiHintWrong ? " (wrong)" : "") + (c.followedAi ? ", followed" : ", not followed"));
            Add("Action", (string.IsNullOrEmpty(c.action) ? "none" : c.action) + (c.group ? " (group decision)" : "") + (c.actionChanges > 0 ? ", changed " + c.actionChanges + "x" : ""));
            Add("Call to action", Sec(c.decisionLatency) + (c.holds > 0 ? ", " + c.holds + " hold(s), " + Sec(c.holdSeconds) + " on hold" : ""));
            Add("Card in force", (string.IsNullOrEmpty(c.roeAtAction) ? "-" : c.roeAtAction) + (c.staleCard ? " (followed the old card)" : ""));
            if (c.clockOpen >= 0f) Add("Clock", "opened at " + Sec(c.clockOpen) + ", deadline " + Sec(c.deadline) + (c.clockMiss ? ", MISSED" : "") + (c.correctRejection ? ", correctly left alone" : ""));
            Add("Outcome", (c.outcome ?? "") + (c.unverified ? " (unverified engagement)" : ""));
            Add("Decision vs result", grid + (string.IsNullOrEmpty(c.severity) ? "" : ", severity " + c.severity));
            Add("Decision quality", c.processScore >= 0 ? c.processScore + " / 100" : "n/a (no decision needed or made)");
            if (c.nodes != null)
                foreach (var n in c.nodes)
                    Add(n.label + (n.passed ? " passed" : " failed"), (n.points >= 0 ? "+" : "") + n.points + " points" + (string.IsNullOrEmpty(n.reason) ? "" : ": " + n.reason));
            Add("Points", c.scored ? c.total.ToString() : "not scored");
            return r;
        }

        // Draws every tree on the page (one per Show scope). Colours match DecisionTreeView.
        public const string Script = @"(function(){
function E(t,c,x){var e=document.createElement(t);if(c)e.className=c;if(x!=null)e.textContent=x;return e;}
var ICON=['&#9635;','&#9776;','&#10140;','&#9635;','&#9635;'];
function draw(box,D,st){
 box.innerHTML='';var C=D.c,n=C.length;if(st.f>=n)st.f=-1;var F=st.f>=0?C[st.f]:null;
 var P=E('div','dt-panel');box.appendChild(P);
 var hd=E('div','dt-head');P.appendChild(hd);
 var res=0;for(var i=0;i<n;i++)res+=C[i].r;
 var cap=E('div','dt-cap');cap.innerHTML='<b>'+(n?Math.round(100*res/n):0)+'%</b> RESOLVED &nbsp;&nbsp; '+n+' CONTACT'+(n==1?'':'S');hd.appendChild(cap);
 hd.appendChild(E('div','dt-rule'));
 hd.appendChild(E('div','dt-title',F?('CONTACT #'+(F.id<10?'0':'')+F.id):'ALL CONTACTS'));
 hd.appendChild(E('div','dt-who',F?F.who:'path share of the whole run'));
 var nav=E('div','dt-nav');hd.appendChild(nav);
 [['ALL',function(){st.f=-1;}],['<',function(){st.f=st.f-1<-1?n-1:st.f-1;}],['>',function(){st.f=st.f+1>=n?-1:st.f+1;}]].forEach(function(b){var e=E('button','',b[0]);e.type='button';e.onclick=function(){b[1]();draw(box,D,st);};nav.appendChild(e);});
 if(!n){P.appendChild(E('p','dt-dim','No contacts in this run.'));return;}
 var G=E('div','dt-grid');P.appendChild(G);
 D.s.forEach(function(S,si){
  var cnt=[],reached=0;S.n.forEach(function(nd,k){var c=0;for(var i=0;i<n;i++)if(C[i].b[si]===nd[0])c++;cnt.push(c);reached+=c;});
  var lit=F?(F.b[si]!=null&&F.b[si]!==''):reached>0;
  var B=E('div','dt-block');G.appendChild(B);
  var bar=E('div','dt-bar'+(lit?'':' off'));bar.innerHTML='<span class=""dt-ic"">'+ICON[si]+'</span>';bar.appendChild(E('span','dt-bt',S.t));bar.appendChild(E('span','dt-fold',st.c[si]?'+':'-'));
  bar.onclick=function(){st.c[si]=!st.c[si];draw(box,D,st);};B.appendChild(bar);
  B.appendChild(E('div','dt-cp',st.c[si]?'CHECKPOINT   '+S.n.length+' branches folded, click to open':'CHECKPOINT   reached by '+reached));
  if(st.c[si])return;
  var L=E('div','dt-list'+(lit?' lit':''));B.appendChild(L);
  S.n.forEach(function(nd,k){var c=cnt[k],pc=n?100*c/n:0,tk=F&&F.b[si]===nd[0],on=F?tk:c>0;
   var r=E('div','dt-node t'+nd[2]+(tk?' tk':on?' on':''));
   r.appendChild(E('span','dt-lab',nd[1]+(c>0?'  ('+c+')':'')));r.appendChild(E('span','dt-pc',Math.round(pc)+'%'));
   if(c>0){var sb=E('i','dt-sh');sb.style.width='calc((100% - 54px) * '+(pc/100)+')';r.appendChild(sb);}
   L.appendChild(r);});
 });
 if(F){
  var H=E('div','dt-detail');P.appendChild(H);
  var hh=E('div','dt-hh');hh.innerHTML='<b>DETAILS</b>';H.appendChild(hh);
  var T=E('table','dt-facts');F.f.forEach(function(kv){var tr=E('tr');tr.appendChild(E('th','',kv[0]));tr.appendChild(E('td','',kv[1]));T.appendChild(tr);});H.appendChild(T);
  var h2=E('div','dt-hh');h2.innerHTML='<b>DECISION HISTORY</b>'+(F.chg?'&nbsp;&nbsp;&nbsp;&nbsp;'+F.chg.replace(/</g,'&lt;'):'');H.appendChild(h2);
  if(!F.h.length)H.appendChild(E('div','dt-dim','No call and no action on this contact.'));
  F.h.forEach(function(l){H.appendChild(E('div',l.indexOf('ANOMALY')>=0?'dt-hl':'dt-dim',l));});
 }
}
var A=document.querySelectorAll('.dtx');for(var i=0;i<A.length;i++){var j=A[i].querySelector('.dt-data');if(!j)continue;var D;try{D=JSON.parse(j.textContent);}catch(e){continue;}
 var box=E('div','dt-host');A[i].appendChild(box);draw(box,D,{f:-1,c:[false,false,false,false,false]});}
})();";

        public const string Css = @"
.dtree{margin-top:16px}
.dt-panel{position:relative;background:#CCD1D9;color:#14181F;border-radius:6px;padding:0 12px 14px;font-family:system-ui,Segoe UI,Arial,sans-serif;overflow:hidden}
.dt-head{position:relative;background:#DDE0E6;margin:0 -12px 12px;padding:6px 12px 10px;min-height:62px}
.dt-cap{font-size:12px;color:#5C6675;letter-spacing:.03em}.dt-cap b{color:#14181F}
.dt-rule{height:2px;background:#14181F;width:min(360px,50%);margin:3px 0}
.dt-title{font-size:26px;font-weight:500;letter-spacing:.02em}
.dt-who{position:absolute;right:12px;top:6px;font-size:12px;color:#5C6675}
.dt-nav{position:absolute;right:12px;top:30px;display:flex;gap:4px}
.dt-nav button{background:#7A808A;color:#fff;border:1px solid #4A4F57;border-radius:3px;font:600 13px system-ui,Segoe UI,Arial,sans-serif;padding:4px 12px;cursor:pointer}
.dt-nav button:hover{background:#5D636C}
.dt-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(230px,1fr));gap:18px 26px}
.dt-block{position:relative}
.dt-block+.dt-block .dt-bar::before{content:'';position:absolute;left:-26px;top:14px;width:26px;height:2px;background:#0F59C7}
.dt-bar{position:relative;display:flex;align-items:center;gap:8px;height:30px;padding:0 8px;background:#0F59C7;color:#fff;font-weight:700;font-size:14px;letter-spacing:.03em;cursor:pointer;box-shadow:3px 3px 0 rgba(0,0,0,.18)}
.dt-bar.off{background:#54667F}
.dt-ic{font-size:13px;width:14px;text-align:center}.dt-bt{flex:1;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.dt-fold{width:16px;text-align:center}
.dt-cp{font-size:11px;color:#5C6675;margin:2px 0 4px;white-space:pre}
.dt-list{position:relative;padding-left:24px}
.dt-list::before{content:'';position:absolute;left:9px;top:0;bottom:11px;width:1px;background:rgba(115,125,140,.7)}
.dt-list.lit::before{width:2px;background:#0F59C7}
.dt-node{position:relative;display:flex;align-items:center;height:22px;margin-bottom:4px;background:#B3B8C2;border-left:4px solid #737D8C;font-size:12px;color:#5C6675}
.dt-node::before{content:'';position:absolute;left:-19px;top:10px;width:15px;height:1px;background:rgba(115,125,140,.7)}
.dt-node.on,.dt-node.tk{color:#14181F}.dt-node.on{background:#9EC2ED}.dt-node.on::before,.dt-node.tk::before{height:2px;background:#0F59C7}
.dt-node.tk{background:#0F59C7;color:#fff;font-weight:700}
.dt-node.t1{border-left-color:#269E4D}.dt-node.t2{border-left-color:#D99419}.dt-node.t3{border-left-color:#D1382E}
.dt-lab{flex:1;padding-left:6px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.dt-pc{width:44px;margin-right:2px;height:18px;line-height:18px;text-align:center;background:#DDE0E6;color:#0F59C7;font-weight:700;font-size:11px}
.dt-sh{position:absolute;left:0;bottom:0;height:3px;background:#0F59C7}.dt-node.tk .dt-sh{background:#fff}
.dt-detail{border-top:1px solid rgba(115,125,140,.7);margin-top:14px;padding-top:6px;font-size:12px}
.dt-hh{margin:6px 0 4px}
.dt-facts{border-collapse:collapse;margin-bottom:6px}.dt-facts th{text-align:left;color:#5C6675;font-weight:600;padding:2px 14px 2px 0;vertical-align:top;white-space:nowrap}.dt-facts td{padding:2px 0}
.dt-dim{color:#5C6675}.dt-hl{color:#14181F;font-weight:600}
@media print{.dt-nav{display:none}}
";
    }
}
