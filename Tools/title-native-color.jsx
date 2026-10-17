// 在原画布上烘焙依赖文档尺寸的 Photoshop 渐变与水纹 SoftLight，不修改源 PSB。
#target photoshop
(function(){
 var source=app.activeDocument;if(source.name!=='游戏ui.psb')throw new Error('当前 Photoshop 文档不是游戏ui.psb');
 var output=new Folder(new File($.fileName).parent.parent.fsName+'/Assets/_Project/Art/Sprites/UI/Title/Yang/Entrance');
 var work,flat,map={},ids=[],oldUnits=app.preferences.rulerUnits,oldDialogs=app.displayDialogs;
 app.preferences.rulerUnits=Units.PIXELS;app.displayDialogs=DialogModes.NO;
 function visit(p){for(var i=0;i<p.layers.length;i++){var l=p.layers[i];map[l.id]=l;ids.push(l.id);if(l.typename==='LayerSet')visit(l);}}
 function visible(list,on){var d=new ActionDescriptor(),a=new ActionList();for(var i=0;i<list.length;i++){var r=new ActionReference();r.putIdentifier(charIDToTypeID('Lyr '),list[i]);a.putReference(r);}d.putList(charIDToTypeID('null'),a);executeAction(charIDToTypeID(on?'Shw ':'Hd  '),d,DialogModes.NO);}
 var jobs=[['01_场景/天空_合成底色.png',[1252,1132,1401,464,944,1114]],['04_水面与倒影/水面.png',[1252,1132,558,1394]]],rows=[];
 try{
  work=source.duplicate('标题原画布校色');visit(work);
  for(var j=0;j<jobs.length;j++){
   app.activeDocument=work;visible(ids,false);visible(jobs[j][1],true);
   flat=work.duplicate('校色部件',true);var b=flat.activeLayer.bounds;
   var x=Math.max(0,Number(b[0])),y=Math.max(0,Number(b[1]));flat.trim(TrimType.TRANSPARENT,true,true,true,true);
   var options=new PNGSaveOptions();flat.saveAs(new File(output.fsName+'/'+jobs[j][0]),options,true,Extension.LOWERCASE);
   rows.push('{"file":"'+jobs[j][0]+'","x":'+x+',"y":'+y+',"width":'+Number(flat.width)+',"height":'+Number(flat.height)+'}');
   flat.close(SaveOptions.DONOTSAVECHANGES);flat=null;
  }
  var result=new File(output.fsName+'/native-color.json');result.encoding='UTF8';result.open('w');result.write('['+rows.join(',')+']');result.close();
  return rows.join('\n');
 }finally{if(flat)flat.close(SaveOptions.DONOTSAVECHANGES);if(work)work.close(SaveOptions.DONOTSAVECHANGES);app.activeDocument=source;app.preferences.rulerUnits=oldUnits;app.displayDialogs=oldDialogs;}
})();
