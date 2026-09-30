"""捏脸表情识别网络（exprnet）。

在「规范表情空间」（ARKit 表情基 51 维）里做表情分类，训练数据可来自 FACS 原型合成与公开集特征，
导出时把捏脸绑定矩阵固化进 ONNX 第一层，供 Unity Sentis 离线推理。设计见 DESIGN.md。

子命令（均支持 --help）：
    python -m exprnet.extract   从图片抽 MediaPipe 表情基特征 → npz
    python -m exprnet.train     训练 + 校准 + 评估
    python -m exprnet.evaluate  重新评估一个训练产物
    python -m exprnet.export    导出 ONNX + 元数据并自检
    python -m exprnet.coverage  绑定覆盖度报告
    python -m exprnet.golden_check  金标开发集 / 测试集护栏检查
"""

__version__ = "0.1.0"
