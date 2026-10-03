(() => {
  if (window.__vrhPlayer) return;
  const style = document.createElement('style');
  style.id = 'vrh-pure-player';
  style.textContent = `
    html.vrh-pure, html.vrh-pure body { background:#05090f!important; overflow:hidden!important; margin:0!important; }
    html.vrh-pure body * { visibility:hidden!important; pointer-events:none!important; }
    html.vrh-pure [data-vrh-ancestor] { transform:none!important; filter:none!important; perspective:none!important; contain:none!important; opacity:1!important; overflow:visible!important; clip-path:none!important; }
    html.vrh-pure video[data-vrh-video] { visibility:visible!important; display:block!important; opacity:1!important; position:fixed!important; left:var(--vrh-left)!important; top:var(--vrh-top)!important; width:var(--vrh-width)!important; height:var(--vrh-height)!important; max-width:none!important; max-height:none!important; min-width:0!important; min-height:0!important; margin:0!important; padding:0!important; border:0!important; object-fit:contain!important; transform:none!important; clip-path:none!important; z-index:2147483647!important; background:#05090f!important; }
  `;
  (document.head || document.documentElement).appendChild(style);
  let active = false, video = null, serial = 0;
  const originalControls = new WeakMap();
  const originalProperties = new Map();
  const viewportProperties=['--vrh-left','--vrh-top','--vrh-width','--vrh-height'];
  const hiddenBranches = new Map();
  function restoreBranch(element,original) {
    if(original.value) element.style.setProperty('display',original.value,original.priority);
    else element.style.removeProperty('display');
  }
  function enforce() {
    if(!active || !video?.isConnected) return;
    const path=new Set();for(let p=video;p;p=p.parentElement)path.add(p);
    // display:none on sibling branches cannot be overridden by a child with visibility:visible.
    // This also blocks dynamic gift menus and closed-shadow overlays without touching video pixels.
    for(const [element,original] of hiddenBranches) {
      if(path.has(element)){restoreBranch(element,original);hiddenBranches.delete(element);}
    }
    let child=video;
    for(let parent=child.parentElement;parent;child=parent,parent=parent.parentElement) {
      for(const sibling of parent.children) {
        if(sibling===child || ['STYLE','SCRIPT','LINK','HEAD'].includes(sibling.tagName))continue;
        if(!hiddenBranches.has(sibling)) hiddenBranches.set(sibling,{value:sibling.style.getPropertyValue('display'),priority:sibling.style.getPropertyPriority('display')});
        if(sibling.style.getPropertyValue('display')!=='none'||sibling.style.getPropertyPriority('display')!=='important') sibling.style.setProperty('display','none','important');
      }
    }
  }
  const videos = () => [...document.querySelectorAll('video')].filter(v => v.videoWidth > 0 && v.videoHeight > 0 && v.readyState >= 2);
  function choose() {
    return videos().sort((a,b) => (Number(!b.paused)*1e9 + b.videoWidth*b.videoHeight) - (Number(!a.paused)*1e9 + a.videoWidth*a.videoHeight))[0];
  }
  function mark(v) {
    if (video === v && v?.hasAttribute('data-vrh-video')) {enforce();return;}
    const bounds=v?.getBoundingClientRect();
    document.querySelectorAll('[data-vrh-ancestor],[data-vrh-video]').forEach(e => { e.removeAttribute('data-vrh-ancestor'); e.removeAttribute('data-vrh-video'); });
    video = v; serial++;
    if (!v) return;
    if(!originalProperties.has(v)) originalProperties.set(v,viewportProperties.map(key=>({key,value:v.style.getPropertyValue(key),priority:v.style.getPropertyPriority(key)})));
    // Preserve the on-page video rectangle. Starting recognition changes the OCR scope,
    // never requests browser fullscreen or expands the displayed player.
    const values=[bounds.left/innerWidth*100+'vw',bounds.top/innerHeight*100+'vh',bounds.width/innerWidth*100+'vw',bounds.height/innerHeight*100+'vh'];
    viewportProperties.forEach((key,index)=>v.style.setProperty(key,values[index]));
    if (!originalControls.has(v)) originalControls.set(v,v.controls);
    v.controls = false;
    v.setAttribute('data-vrh-video','1');
    for (let p=v.parentElement;p;p=p.parentElement) p.setAttribute('data-vrh-ancestor','1');
    enforce();
  }
  function state() {
    if (active && (!video || !video.isConnected || video.readyState < 2)) mark(choose());
    if(active)enforce();
    const v = active ? video : choose();
    if (!v) return {ok:false, reason:'没有找到已加载的直播视频，请先打开直播间并开始播放。'};
    const r=v.getBoundingClientRect();
    const scale=Math.min(r.width/v.videoWidth, r.height/v.videoHeight);
    const width=v.videoWidth*scale, height=v.videoHeight*scale;
    const x=r.left+(r.width-width)/2,y=r.top+(r.height-height)/2;
    return {ok:width>0&&height>0, pure:active&&document.documentElement.classList.contains('vrh-pure')&&v.hasAttribute('data-vrh-video'), playing:!v.paused&&!v.ended&&v.readyState>=2, serial, x,y,width,height, viewportWidth:innerWidth,viewportHeight:innerHeight, videoWidth:v.videoWidth,videoHeight:v.videoHeight};
  }
  window.__vrhPlayer = {
    enter() {
      const v=choose();
      if (!v) return {ok:false,reason:'没有找到已加载的直播视频，请先播放直播。'};
      active=true; mark(v); document.documentElement.classList.add('vrh-pure');
      return state();
    },
    state,
    exit() {
      active=false; document.documentElement.classList.remove('vrh-pure');
      for(const [element,original] of hiddenBranches)restoreBranch(element,original);
      hiddenBranches.clear();
      for(const [element,properties] of originalProperties)for(const original of properties){if(original.value)element.style.setProperty(original.key,original.value,original.priority);else element.style.removeProperty(original.key);}
      originalProperties.clear();
      if(video && originalControls.has(video)) video.controls=originalControls.get(video);
      document.querySelectorAll('[data-vrh-ancestor],[data-vrh-video]').forEach(e => {e.removeAttribute('data-vrh-ancestor');e.removeAttribute('data-vrh-video');});
      video=null; return true;
    }
  };
  const observer=new MutationObserver(()=>{if(active)enforce();});
  observer.observe(document.documentElement,{childList:true,subtree:true,attributes:true,attributeFilter:['style','class']});
  // All DOM layers, including canvas/SVG bullet comments, stay invisible. Stream pixels are untouched.
})();
