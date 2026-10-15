// 用原有色标覆盖可见纸面，再在源画布渲染；只写项目预览，不写回 PSB。
#target photoshop
(function(){
 var source=app.activeDocument;
 if(source.name!=='游戏ui.psb')throw new Error('当前文档不是游戏ui.psb');
 var output=new Folder(new File($.fileName).parent.parent.fsName+'/outputs/title-selected-visible');output.create();
 var jobs=[['settings',1476,1470],['load',935,869],['continue',1500,1494],['quit',1468,1462]];
 var map={},ids=[],work,flat,rows=[],oldUnits=app.preferences.rulerUnits,oldDialogs=app.displayDialogs;
 app.preferences.rulerUnits=Units.PIXELS;app.displayDialogs=DialogModes.NO;
 function visit(parent){for(var i=0;i<parent.layers.length;i++){var l=parent.layers[i];map[l.id]=l;ids.push(l.id);if(l.typename==='LayerSet')visit(l);}}
 function children(layer,list){list.push(layer.id);if(layer.typename==='LayerSet')for(var i=0;i<layer.layers.length;i++)children(layer.layers[i],list);}
 function visible(list,on){var d=new ActionDescriptor(),a=new ActionList();for(var i=0;i<list.length;i++){var r=new ActionReference();r.putIdentifier(charIDToTypeID('Lyr '),list[i]);a.putReference(r);}d.putList(charIDToTypeID('null'),a);executeAction(charIDToTypeID(on?'Shw ':'Hd  '),d,DialogModes.NO);}
 try{
  work=source.duplicate('选中卷轴原画布校色');visit(work);
  for(var j=0;j<jobs.length;j++){
   app.activeDocument=work;visible(ids,false);
   var group=map[jobs[j][1]],show=[];children(group,show);
   var parent=group.parent;while(parent.typename==='LayerSet'){show.push(parent.id);parent=parent.parent;}
   visible(show,true);
   var ref=new ActionReference();ref.putIdentifier(charIDToTypeID('Lyr '),jobs[j][2]);
   var info=executeActionGet(ref),bounds=info.getObjectValue(stringIDToTypeID('boundsNoEffects'));
   var top=bounds.getUnitDoubleValue(stringIDToTypeID('top')),bottom=bounds.getUnitDoubleValue(stringIDToTypeID('bottom'));
   var height=bottom-top,visibleTop=Math.max(0,top),effects=info.getObjectValue(stringIDToTypeID('layerEffects'));
   var gradient=effects.getObjectValue(stringIDToTypeID('gradientFill')),offset=new ActionDescriptor();
   gradient.putUnitDouble(stringIDToTypeID('scale'),charIDToTypeID('#Prc'),(bottom-visibleTop)/height*100);
   offset.putUnitDouble(stringIDToTypeID('horizontal'),charIDToTypeID('#Prc'),0);
   offset.putUnitDouble(stringIDToTypeID('vertical'),charIDToTypeID('#Prc'),(visibleTop-top)/height*50);
   gradient.putObject(stringIDToTypeID('offset'),stringIDToTypeID('paint'),offset);
   effects.putObject(stringIDToTypeID('gradientFill'),stringIDToTypeID('gradientFill'),gradient);
   var target=new ActionReference();target.putProperty(charIDToTypeID('Prpr'),stringIDToTypeID('layerEffects'));target.putIdentifier(charIDToTypeID('Lyr '),jobs[j][2]);
   var change=new ActionDescriptor();change.putReference(charIDToTypeID('null'),target);change.putObject(charIDToTypeID('T   '),stringIDToTypeID('layerEffects'),effects);
   executeAction(charIDToTypeID('setd'),change,DialogModes.NO);
   flat=work.duplicate('选中卷轴输出',true);
   flat.trim(TrimType.TRANSPARENT,true,true,true,true);
   flat.saveAs(new File(output.fsName+'/ui_title_'+jobs[j][0]+'_selected.png'),new PNGSaveOptions(),true,Extension.LOWERCASE);
   rows.push(jobs[j][0]+': '+Number(flat.width)+'x'+Number(flat.height));
   flat.close(SaveOptions.DONOTSAVECHANGES);flat=null;
  }
  return rows.join('\n');
 }finally{
  if(flat)flat.close(SaveOptions.DONOTSAVECHANGES);
  if(work)work.close(SaveOptions.DONOTSAVECHANGES);
  app.activeDocument=source;app.preferences.rulerUnits=oldUnits;app.displayDialogs=oldDialogs;
 }
})();
