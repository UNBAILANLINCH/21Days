// 用原 PSB 的图层样式烘焙卷轴底，保留原坐标和尺寸；源文档不写回。
#target photoshop
(function(){
 var source=app.activeDocument;
 if(source.name!=='游戏ui.psb')throw new Error('当前文档不是游戏ui.psb');
 var output=new Folder(new File($.fileName).parent.parent.fsName+'/Assets/_Project/Art/Sprites/UI/Title/Yang/Entrance/05_卷轴按钮');
 var jobs=[['设置',928,92,-361,227,624,929],['选择存档',1425,414,-239,227,624,1424],['继续',874,1305,-239,227,624,881],['退出游戏',919,1600,-366,236,634,920]];
 var map={},ids=[],work,flat,oldUnits=app.preferences.rulerUnits,oldDialogs=app.displayDialogs,rows=[];
 app.preferences.rulerUnits=Units.PIXELS;app.displayDialogs=DialogModes.NO;
 function visit(parent){for(var i=0;i<parent.layers.length;i++){var layer=parent.layers[i];map[layer.id]=layer;ids.push(layer.id);if(layer.typename==='LayerSet')visit(layer);}}
 function visible(list,on){var d=new ActionDescriptor(),a=new ActionList();for(var i=0;i<list.length;i++){var r=new ActionReference();r.putIdentifier(charIDToTypeID('Lyr '),list[i]);a.putReference(r);}d.putList(charIDToTypeID('null'),a);executeAction(charIDToTypeID(on?'Shw ':'Hd  '),d,DialogModes.NO);}
 try{
  work=source.duplicate('卷轴原填充烘焙');visit(work);
  for(var j=0;j<jobs.length;j++){
   app.activeDocument=work;visible(ids,false);
   var job=jobs[j],show=[job[1],job[6]],layer=map[job[1]];
   if(!layer)throw new Error('找不到卷轴图层 '+job[1]);
   var parent=layer.parent;while(parent.typename==='LayerSet'){show.push(parent.id);parent=parent.parent;}
   visible(show,true);
   flat=work.duplicate('卷轴填充输出',true);
   // 先在原画布合并样式，再补回画面外的上半截，避免渐变重算。
   flat.resizeCanvas(Number(flat.width),Number(flat.height)-job[3],AnchorPosition.BOTTOMCENTER);
   flat.crop([job[2],0,job[2]+job[4],job[5]]);
   flat.saveAs(new File(output.fsName+'/'+job[0]+'/未选中/卷轴底.png'),new PNGSaveOptions(),true,Extension.LOWERCASE);
   rows.push(job[0]+': '+Number(flat.width)+'x'+Number(flat.height));
   flat.close(SaveOptions.DONOTSAVECHANGES);flat=null;
  }
  return rows.join('\n');
 }finally{
  if(flat)flat.close(SaveOptions.DONOTSAVECHANGES);
  if(work)work.close(SaveOptions.DONOTSAVECHANGES);
  app.activeDocument=source;app.preferences.rulerUnits=oldUnits;app.displayDialogs=oldDialogs;
 }
})();
