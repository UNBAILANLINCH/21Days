// 现有标题导出脚本绑定 PSB 图层 ID；角色 PSD 改用顶层拆件和原画布，不扩展运行时代码。
// 调用方提供 characterSources（路径数组）与 characterIds；只操作副本，源文件不保存。
#target photoshop
(function () {
    if (characterSources.length !== characterIds.length) throw new Error('源文件与角色数量不一致');
    var root = new Folder(new File($.fileName).parent.parent.fsName + '/Assets/_Project/Art/Sprites/Characters');
    var oldUnits = app.preferences.rulerUnits, oldDialogs = app.displayDialogs, previous = app.documents.length ? app.activeDocument : null;
    var work = null, flat = null, summary = [];
    function quote(value) { return '"' + String(value).replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\r/g, '\\r').replace(/\n/g, '\\n') + '"'; }
    function folder(value) { if (!value.exists && !value.create()) throw new Error('不能创建目录：' + value.fsName); }
    function savePng(doc, file) { var options = new PNGSaveOptions(); options.compression = 9; doc.saveAs(file, options, true, Extension.LOWERCASE); if (!file.exists) throw new Error('导出失败：' + file.name); }
    app.preferences.rulerUnits = Units.PIXELS;
    app.displayDialogs = DialogModes.NO;
    try {
        for (var s = 0; s < characterSources.length; s++) {
            if (!/^[a-z_]+$/.test(characterIds[s])) throw new Error('角色 ID 非法');
            var source = app.open(new File(characterSources[s]));
            if (Number(source.width) !== 512 || Number(source.height) !== 512) throw new Error('源画布应为 512×512');
            var output = new Folder(root.fsName + '/' + characterIds[s]); folder(output);
            var parts = new Folder(output.fsName + '/parts'); folder(parts);
            work = source.duplicate('角色拆件导出_' + characterIds[s]);
            var layers = [], rows = [];
            for (var i = 0; i < work.layers.length; i++) {
                var layer = work.layers[i];
                if (layer.typename !== 'ArtLayer') throw new Error('此交付包含分组，需要先核实分组拆件');
                if (layer.visible && !layer.isBackgroundLayer) layers.push(layer);
            }
            if (!layers.length) throw new Error('没有可见拆件');
            flat = work.duplicate('角色合成预览', true);
            savePng(flat, new File(output.fsName + '/composite.png'));
            flat.close(SaveOptions.DONOTSAVECHANGES); flat = null;
            for (var h = 0; h < work.layers.length; h++) work.layers[h].visible = false;
            for (var j = 0; j < layers.length; j++) {
                app.activeDocument = work;
                layers[j].visible = true;
                var name = ('0' + (j + 1)).slice(-2) + '_' + layers[j].name.replace(/[\\\/:*?"<>|]/g, '_') + '.png';
                flat = work.duplicate('角色单件', true);
                savePng(flat, new File(parts.fsName + '/' + name));
                flat.close(SaveOptions.DONOTSAVECHANGES); flat = null;
                rows.push('{"file":' + quote('parts/' + name) + ',"layer":' + quote(layers[j].name) + ',"sortingOrder":' + (layers.length - 1 - j) + '}');
                layers[j].visible = false;
            }
            var manifest = new File(output.fsName + '/parts.json'); manifest.encoding = 'UTF8';
            if (!manifest.open('w')) throw new Error('不能写入拆件清单');
            manifest.write('{"source":' + quote(source.name) + ',"width":512,"height":512,"layers":[' + rows.join(',') + ']}'); manifest.close();
            summary.push(characterIds[s] + ': ' + layers.length + ' 件');
            work.close(SaveOptions.DONOTSAVECHANGES); work = null;
        }
        return summary.join('\n');
    } finally {
        if (flat) flat.close(SaveOptions.DONOTSAVECHANGES);
        if (work) work.close(SaveOptions.DONOTSAVECHANGES);
        app.preferences.rulerUnits = oldUnits; app.displayDialogs = oldDialogs;
        if (previous) app.activeDocument = previous;
    }
})();
