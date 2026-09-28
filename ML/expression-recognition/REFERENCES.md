# 参考文献核对报告

核对对象：`DESIGN.md`（2026-09-28 版本）引用的文献与工程事实。逐条核查作者、标题、会议/期刊、年份，给出可访问链接，并标注 DESIGN.md 里的引用位置与本项目用途。**凡是核对不到官方来源的，都在条目里明写「找不到」，不编造。**

---

## 1·表情理论与 FACS

- **Ekman, P., & Friesen, W. V. (1978). *Facial Action Coding System (FACS): A Technique for the Measurement of Facial Movement*. Consulting Psychologists Press, Palo Alto.**
  链接：无免费全文（专著，仅可购买）；概览见 [Wikipedia: Facial Action Coding System](https://en.wikipedia.org/wiki/Facial_Action_Coding_System)、[Paul Ekman Group 官方页](https://www.paulekman.com/facial-action-coding-system/)。
  用途：DESIGN.md §3.1 提到的 FACS 动作单元（AU）体系与 A–E 强度档的原始出处；本项目合成器按 AU 强度档采样表情原型即依据此系统。

- **Ekman, P., Friesen, W. V., & Hager, J. C. (2002). *Facial Action Coding System: The Manual and Investigator's Guide*. Research Nexus, Salt Lake City, UT.**
  链接：无免费全文；购买/信息页同上 [Paul Ekman Group](https://www.paulekman.com/facial-action-coding-system/)；被 CK+ 论文引用为参考文献 [9]（见下方 CK+ 条目的 PDF）。
  用途：DESIGN.md §3.1「EMFACS 情绪–AU 对应」的源头——CK+ 论文明确说明其 Table 2 判定规则是「与 FACS manual 的 Emotion Prediction Table 比对」后确定的（见附录 A）。本项目合成器的情绪原型（高兴/悲伤/惊讶等）AU 组合即参照此表的思路。

- **Du, S., Tao, Y., & Martinez, A. M. (2014). Compound facial expressions of emotion. *PNAS*, 111(15), E1454–E1462.**
  链接：[PNAS 官方页 (DOI: 10.1073/pnas.1322355111)](https://www.pnas.org/doi/abs/10.1073/pnas.1322355111)
  用途：DESIGN.md §3.2 引用为「复合表情（又惊又喜）」的心理学依据；核对无误——该文正是提出「复合情绪类别」（如 happily surprised）的原始研究。

- **Jack, R. E., Garrod, O. G. B., Yu, H., Caldara, R., & Schyns, P. G. (2012). Facial expressions of emotion are not culturally universal. *PNAS*, 109(19), 7241–7244.**
  链接：[PNAS 官方页 (DOI: 10.1073/pnas.1200155109)](https://www.pnas.org/doi/10.1073/pnas.1200155109)；开放 PDF：[perso.unifr.ch 镜像](https://perso.unifr.ch/roberto.caldara/pdfs/jack_12.pdf)
  用途：DESIGN.md §7 引用「东亚观察者判读表情更依赖眼部区域」——**核对结果：准确**。该文明确指出东亚观察者在判读恐惧/厌恶等表情时「persistently fixating the eye region」而非像西方观察者那样在全脸均匀分布注视，因而更容易混淆表情；这是本项目「金标集标注者应以目标玩家（东亚）为主」的依据。

---

## 2·参数空间表情识别

- **Grishchenko, I., Yan, G., Bazavan, E. G., Zanfir, A., Chinaev, N., Raveendran, K., Grundmann, M., & Sminchisescu, C. (2023). Blendshapes GHUM: Real-time Monocular Facial Blendshape Prediction. *arXiv:2309.05782*.**
  链接：[arXiv:2309.05782](https://arxiv.org/abs/2309.05782)；模型卡：[MediaPipe Blendshape V2 Model Card](https://storage.googleapis.com/mediapipe-assets/Model%20Card%20Blendshape%20V2.pdf)
  许可证：Apache License 2.0（模型卡明确写「LICENSED UNDER: Apache License, Version 2.0」）。
  用途：DESIGN.md §2 引用为 MediaPipe Face Landmarker 能从单张照片回归 52 维表情基系数的技术来源；公开集图片经此模型转成「参数向量+标签」是本项目数据管线的第一步。

- **MediaPipe Face Landmarker 的 52 个 blendshape 类别名与许可证**——核对结果见附录 B（含与「ARKit 52 个，去掉 tongueOut 余 51 个」表述的对照说明）。

- **Danecek, R., Black, M. J., & Bolkart, T. (2022). EMOCA: Emotion Driven Monocular Face Capture and Animation. *CVPR 2022*, 20311–20322.**
  链接：[CVF Open Access](https://openaccess.thecvf.com/content/CVPR2022/html/Danecek_EMOCA_Emotion_Driven_Monocular_Face_Capture_and_Animation_CVPR_2022_paper.html)；[arXiv:2204.11312](https://arxiv.org/abs/2204.11312)
  用途：DESIGN.md §2 引用为「在参数空间里识别表情」的先例。EMOCA 的主任务是「图像 → 3D 表情系数」的重建，并用情绪识别网络的感知损失监督重建；同时论文在重建出的几何参数上做了情绪识别，摘要原话：“On the task of in-the-wild emotion recognition, our purely geometric approach is on par with the best image-based methods”。本项目引用的是后者。

- **Aneja, D., Chaudhuri, B., Colburn, A., Faigin, G., Shapiro, L., & Mones, B. (2018). Learning to Generate 3D Stylized Character Expressions from Humans. *WACV 2018*, 160–169.**
  链接：官方无免费 PDF（WACV 2018 早于 CVF 开放获取覆盖范围，需 IEEE Xplore）；摘要与图表见 [Semantic Scholar](https://www.semanticscholar.org/paper/Learning-to-Generate-3D-Stylized-Character-from-Aneja-Chaudhuri/525da67fb524d46f2afa89478cd482a68be8a42b)、[ResearchGate](https://www.researchgate.net/publication/324361168_Learning_to_Generate_3D_Stylized_Character_Expressions_from_Humans)；代码：[GitHub: bindita/ExprGen](https://github.com/bindita/ExprGen)
  用途：DESIGN.md §2 引用为 ExprGen——在风格化角色绑定参数空间做表情感知一致性约束的先例，核对基本准确。

- **Aneja, D., Colburn, A., Faigin, G., Shapiro, L., & Mones, B. (2016). Modeling Stylized Character Expressions via Deep Learning. *ACCV 2016*.**
  链接：开放 PDF：[ece.uw.edu](https://www.ece.uw.edu/wp-content/uploads/2017/04/deepali_accv2016.pdf)、[homes.cs.washington.edu](https://homes.cs.washington.edu/~shapiro/Deepali1.pdf)；出版页：[Springer](https://link.springer.com/chapter/10.1007/978-3-319-54184-6_9)；代码/数据：[GitHub: deepalianeja/CharacterExpr](https://github.com/deepalianeja/CharacterExpr)
  用途：DESIGN.md §2 引用为 DeepExpr / FERG-DB——真人与风格化角色共享表情特征空间的先例，核对基本准确（该文提出 FERG-DB 数据库和 DeepExpr 感知模型）。

---

## 3·网络结构

- **Gorishniy, Y., Rubachev, I., Khrulkov, V., & Babenko, A. (2021). Revisiting Deep Learning Models for Tabular Data. *NeurIPS 2021*.**
  链接：[arXiv:2106.11959](https://arxiv.org/abs/2106.11959)；[NeurIPS 官方页](https://proceedings.neurips.cc/paper/2021/hash/9d86d83f925f2149e9edb0ac3b49229c-Abstract.html)
  用途：DESIGN.md §4 引用为 `regionformer` 网络（FT-Transformer 思路：每个表情基当 token）的设计依据，核对无误。

- **Luo, C., Song, S., Xie, W., Shen, L., & Gunes, H. (2022). Learning Multi-dimensional Edge Feature-based AU Relation Graph for Facial Action Unit Recognition. *IJCAI 2022*, 1239–1246.**
  链接：[IJCAI 官方页（含 PDF）](https://www.ijcai.org/proceedings/2022/173)；[arXiv:2205.01782](https://arxiv.org/abs/2205.01782)；代码：[GitHub: CVI-SZU/ME-GraphAU](https://github.com/CVI-SZU/ME-GraphAU)
  用途：DESIGN.md §4 引用为 AU 共现/互斥关系图建模的先例（ME-GraphAU），核对无误。

- **Li, G., Zhu, X., Zeng, Y., Wang, Q., & Lin, L. (2019). Semantic Relationships Guided Representation Learning for Facial Action Unit Recognition. *AAAI 2019*, 33(01), 8594–8601.**
  链接：[AAAI 官方页](https://ojs.aaai.org/index.php/AAAI/article/view/4879)
  用途：DESIGN.md §4 引用为 SRERL——用 AU 关系图（Gated Graph Neural Network）建模同一件事的先例，核对无误。

---

## 4·训练与鲁棒性

- **Wang, K., Peng, X., Yang, J., Lu, S., & Qiao, Y. (2020). Suppressing Uncertainties for Large-Scale Facial Expression Recognition (SCN). *CVPR 2020*, 6897–6906.**
  链接：[CVF Open Access PDF](https://openaccess.thecvf.com/content_CVPR_2020/papers/Wang_Suppressing_Uncertainties_for_Large-Scale_Facial_Expression_Recognition_CVPR_2020_paper.pdf)
  用途：DESIGN.md §5 引用为「公开 FER 数据标注噪声大」的动机来源（主损失用软标签+标签平滑），核对无误。

- **Menon, A. K., Jayasumana, S., Rawat, A. S., Jain, H., Veit, A., & Kumar, S. (2021). Long-tail learning via logit adjustment. *ICLR 2021*.**
  链接：[arXiv:2007.07314](https://arxiv.org/abs/2007.07314)；[OpenReview](https://openreview.net/forum?id=37nvvqkCo5)
  用途：DESIGN.md §5 引用为类别不平衡的 logit adjustment 依据，核对无误。**附带核实**：DESIGN.md 提到「FER2013 的厌恶类只占约 1.5%」——按公开的 FER2013 类别计数（disgust 547 / 总计 35,887）约为 1.52%，与文档所述相符。

- **Hendrycks, D., Mazeika, M., & Dietterich, T. (2019). Deep Anomaly Detection with Outlier Exposure. *ICLR 2019*.**
  链接：[arXiv:1812.04606](https://arxiv.org/abs/1812.04606)；代码：[GitHub: hendrycks/outlier-exposure](https://github.com/hendrycks/outlier-exposure)
  用途：DESIGN.md §5 引用为「认不出」负样本训练（Outlier Exposure）的依据，核对无误。

- **Wang, Y., Ma, X., Chen, Z., Luo, Y., Yi, J., & Bailey, J. (2019). Symmetric Cross Entropy for Robust Learning with Noisy Labels. *ICCV 2019*, 322–330.**
  链接：[CVF Open Access PDF](https://openaccess.thecvf.com/content_ICCV_2019/papers/Wang_Symmetric_Cross_Entropy_for_Robust_Learning_With_Noisy_Labels_ICCV_2019_paper.pdf)；[arXiv:1908.06112](https://arxiv.org/abs/1908.06112)
  用途：DESIGN.md §5 引用为可选的对称交叉熵（SCE）开关，核对无误。

- **Zhang, H., Cisse, M., Dauphin, Y. N., & Lopez-Paz, D. (2018). mixup: Beyond Empirical Risk Minimization. *ICLR 2018*.**
  链接：[arXiv:1710.09412](https://arxiv.org/abs/1710.09412)；代码：[GitHub: facebookresearch/mixup-cifar10](https://github.com/facebookresearch/mixup-cifar10)
  用途：DESIGN.md §3.2 引用为「捏脸常是混合表情」的 mixup + 软标签训练依据，核对无误。

- **Geng, X. (2016). Label Distribution Learning. *IEEE Transactions on Knowledge and Data Engineering*, 28(7), 1734–1748. DOI: 10.1109/TKDE.2016.2545658.**
  链接：作者主页 [palm.seu.edu.cn/xgeng/LDL](https://palm.seu.edu.cn/xgeng/LDL/index.htm)（含论文与代码下载）；DOI 检索：[Google Scholar 条目](https://scholar.google.com/scholar_lookup?doi=10.1109/TKDE.2016.2545658)
  用途：DESIGN.md §3.2 引用为按标签分布学习（软标签）的理论依据，核对无误。

---

## 5·校准与 OOD

- **Guo, C., Pleiss, G., Sun, Y., & Weinberger, K. Q. (2017). On Calibration of Modern Neural Networks. *ICML 2017*.**
  链接：[arXiv:1706.04599](https://arxiv.org/abs/1706.04599)
  用途：DESIGN.md §6 引用为温度缩放校准的依据，核对无误。

- **Liu, W., Wang, X., Owens, J. D., & Li, Y. (2020). Energy-based Out-of-distribution Detection. *NeurIPS 2020*.**
  链接：[arXiv:2010.03759](https://arxiv.org/abs/2010.03759)
  用途：DESIGN.md §6 引用为能量分数（energy score）判「认不出」的依据，核对无误。

---

## 6·可扩展方向（本期不做，仅引用核对）

- **Snell, J., Swersky, K., & Zemel, R. (2017). Prototypical Networks for Few-shot Learning. *NeurIPS 2017*, 4077–4087.**
  链接：[arXiv:1703.05175](https://arxiv.org/abs/1703.05175)
  用途：DESIGN.md §10 引用为策划自定义类别少样本分类的备选方案，核对无误。

- **Hinton, G., Vinyals, O., & Dean, J. (2015). Distilling the Knowledge in a Neural Network. *arXiv:1503.02531*.**
  链接：[arXiv:1503.02531](https://arxiv.org/abs/1503.02531)
  用途：DESIGN.md §10 引用为知识蒸馏给公开集生成软标签的依据，核对无误。

- **图像 FER 对照（仅作背景，本方案不使用）**：
  - Mao, J., Xu, R., Yin, X., Chang, Y., Nie, B., & Huang, A. (2023). POSTER++: A simpler and stronger facial expression recognition network. *arXiv:2301.12149*. 链接：[arXiv:2301.12149](https://arxiv.org/abs/2301.12149)
  - Zhang, S., Zhang, Y., Zhang, Y., Wang, Y., & Song, Z. (2023). A Dual-Direction Attention Mixed Feature Network for Facial Expression Recognition. *Electronics*, 12(17), 3595. 链接：[MDPI](https://www.mdpi.com/2079-9292/12/17/3595)；代码：[GitHub: SainingZhang/DDAMFN](https://github.com/SainingZhang/DDAMFN)
  - Xue, F., Wang, Q., Tan, Z., Ma, Z., & Guo, G. (2022). Vision Transformer with Attentive Pooling for Robust Facial Expression Recognition (APViT). *IEEE Transactions on Affective Computing*. DOI: 10.1109/TAFFC.2022.3226473. 链接：[arXiv:2212.05463](https://arxiv.org/abs/2212.05463)

---

## 7·数据集与授权

| 数据集 | 引用 | 获取方式 | 许可证 / 使用条款 | 是否可商用 |
| --- | --- | --- | --- | --- |
| **CK+** | Lucey, P., Cohn, J. F., Kanade, T., Saragih, J., Ambadar, Z., & Matthews, I. (2010). The Extended Cohn-Kanade Dataset (CK+): A complete dataset for action unit and emotion-specified expression. *CVPRW 2010*, 94–101. [作者 PDF](https://iainm.com/assets/pdf/Lucey-2010.pdf) | 需申请：访问 [CMU 官方页](http://vasc.ri.cmu.edu/idb/html/face/facial_expression/) 下载协议表并签署寄回，约 4–5 个工作日后收到下载说明 | 论文原文未见通用许可证文本，仅通过签署使用协议获取；未见「禁止商用」以外的明确豁免条款 | 论文本身未声明可商用；按惯例视为仅限研究 |
| **FER2013** | Goodfellow, I. J., et al. (2013). Challenges in Representation Learning: A report on three machine learning contests. *arXiv:1307.0414* / *Neural Networks*, 64, 59–63. | 免费直接下载，多个 Kaggle 镜像（如 `msambare/fer2013`），无需申请 | **找不到官方权威许可证声明**——原始图片经 Google 图片搜索抓取而来（Goodfellow et al. 论文自述），底层图片版权归属不明；各 Kaggle 二次上传页自行标注的许可证（CC0 等）系上传者个人声明，不构成原始版权方授权 | **不建议商用**；DESIGN.md 已将其列入「仅限非商用研究」，与本核对结论一致 |
| **RAF-DB** | Li, S., Deng, W., & Du, J. (2017). Reliable Crowdsourcing and Deep Locality-Preserving Learning for Expression Recognition in the Wild. *CVPR 2017*. [CVF PDF](https://openaccess.thecvf.com/content_cvpr_2017/papers/Li_Reliable_Crowdsourcing_and_CVPR_2017_paper.pdf) | 需申请：邮件联系作者获取密码，见[官方页](http://www.whdeng.cn/RAF/model1.html) | 使用协议明确：仅限研究用途，不得转让第三方，不得转售或用于盈利 | 不可商用 |
| **AffectNet** | Mollahosseini, A., Hasani, B., & Mahoor, M. H. (2017/2019). AffectNet: A Database for Facial Expression, Valence, and Arousal Computing in the Wild. *IEEE TAC*, 10(1), 18–31. DOI: 10.1109/TAFFC.2017.2740923. [arXiv:1708.03985](https://arxiv.org/abs/1708.03985) | 需申请：[官方页](http://mohammadmahoor.com/affectnet/) 提交请求（机构邮箱等） | 官方声明仅限研究用途（research purposes only） | 不可商用 |
| **KDEF** | Lundqvist, D., Flykt, A., & Öhman, A. (1998). The Karolinska Directed Emotional Faces – KDEF. CD ROM, Karolinska Institutet. | 需注册：[kdef.se](https://kdef.se/) 官网注册页下载 | [使用条款](https://kdef.se/faq/using-and-publishing-kdef-and-akdef) 明确：仅限非商用科研用途；不得再分发；不得在 App / 在线服务中公开使用/再分发 | 不可商用 |
| **JAFFE** | Lyons, M., Akamatsu, S., Kamachi, M., & Gyoba, J. (1998). Coding facial expressions with Gabor wavelets. *FG 1998*.（数据集本体见 [kasrl.org](https://www.kasrl.org/jaffe.html)） | 可直接下载：[Zenodo 存档](https://zenodo.org/records/14974867) 或 [kasrl.org](https://www.kasrl.org/jaffe_download.html) | 仅限非商用科研用途；禁止再分发（含 GitHub/Kaggle/Colab 等）、禁止公开展示/传播；科研出版物中最多展示不超过 10 张样图 | 不可商用 |

---

## 8·工程

- **Unity Sentis 2.1.3 的最低 Unity 版本要求**：官方 `package.json`（`needle-mirror/com.unity.sentis`，与 Unity 官方镜像同步）明确写 `"version": "2.1.3"`、`"unity": "2022.3"`、`"unityRelease": "11f1"`，即最低支持 **Unity 2022.3.11f1**。
  链接：[package.json 源码](https://github.com/needle-mirror/com.unity.sentis/blob/master/package.json)
  用途：DESIGN.md §1 表格「部署」一行的版本要求，核对无误。

- **Sentis 支持的 ONNX opset 范围**：官方文档原文「Sentis supports most ONNX model files with an opset version between 7 and 15.」
  链接：[Sentis Manual: Supported ONNX models](https://docs.unity3d.com/Packages/com.unity.sentis@2.1/manual/supported-models.html)
  用途：DESIGN.md §1、§8 提到的 opset 7–15 与导出 opset 15，核对无误。

- **ARKit 52 个 blendshape 与 FACS AU 的对应关系**：见附录 C。来源为 Melinda Ozel（FACS/面部动画从业者，自述曾与 FaceShift 前员工核对 ARKit blendshape 的原始定义）整理的 [ARKit to FACS Blendshape Cheat Sheet](https://melindaozel.com/arkit-to-facs-cheat-sheet/)。**这不是 Apple 官方文档，也非同行评审文献**，是业内非正式但常被引用的对照表；DESIGN.md 举例的几组对应（browInnerUp≈AU1、mouthSmileLeft/Right≈AU12、cheekSquint≈AU6、noseSneer≈AU9、jawOpen≈AU26/27）与该表一致。

---

## 附录 A · CK+ 论文 Table 1 / Table 2 原文整理

来源：Lucey et al. 2010, *The Extended Cohn-Kanade Dataset (CK+)*，[作者 PDF](https://iainm.com/assets/pdf/Lucey-2010.pdf)，第 3 页。

### Table 1：CK+ 数据库中人工 FACS 编码的 AU 出现频次（峰值帧）

| AU | Name | N | AU | Name | N | AU | Name | N |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Inner Brow Raiser | 173 | 13 | Cheek Puller | 2 | 25 | Lips Part | 287 |
| 2 | Outer Brow Raiser | 116 | 14 | Dimpler | 29 | 26 | Jaw Drop | 48 |
| 4 | Brow Lowerer | 191 | 15 | Lip Corner Depressor | 89 | 27 | Mouth Stretch | 81 |
| 5 | Upper Lid Raiser | 102 | 16 | Lower Lip Depressor | 24 | 28 | Lip Suck | 1 |
| 6 | Cheek Raiser | 122 | 17 | Chin Raiser | 196 | 29 | Jaw Thrust | 1 |
| 7 | Lid Tightener | 119 | 18 | Lip Puckerer | 9 | 31 | Jaw Clencher | 3 |
| 9 | Nose Wrinkler | 74 | 20 | Lip Stretcher | 77 | 34 | Cheek Puff | 1 |
| 10 | Upper Lip Raiser | 21 | 21 | Neck Tightener | 3 | 38 | Nostril Dilator | 29 |
| 11 | Nasolabial Deepener | 33 | 23 | Lip Tightener | 59 | 39 | Nostril Compressor | 16 |
| 12 | Lip Corner Puller | 111 | 24 | Lip Pressor | 57 | 43 | Eyes Closed | 9 |

### Table 2：情绪的 AU 判定标准（论文原文逐字翻译，**这是 CK+ 论文自己的操作化判据，不是「每种情绪的经典 AU 组合示例」**）

| 情绪 | 判定标准（原文） |
| --- | --- |
| Angry（愤怒） | AU23 与 AU24 必须同时出现在 AU 组合中 |
| Disgust（厌恶） | AU9 或 AU10 至少出现一个 |
| Fear（恐惧） | 必须出现 AU1+2+4 组合；例外：若 AU5 强度为 E 级，则 AU4 可缺席 |
| Happy（高兴） | 必须出现 AU12（**不要求 AU6**） |
| Sadness（悲伤） | 必须出现 AU1+4+15 **或** AU11；例外情况为 AU6+15 |
| Surprise（惊讶） | 必须出现 AU1+2 **或** AU5，且 AU5 强度不得超过 B 级（**未提及 AU26**） |
| Contempt（蔑视） | 必须出现 AU14（单侧或双侧均可） |

论文 Figure 1 的图例（非 Table 2 判定标准，只是插图示例）另给出：Disgust=AU1+4+15+17、Happy=AU6+12+25、Surprise=AU1+2+5+25+27、Fear=AU1+4+7+20、Angry=AU4+5+15+17、Sadness=AU1+2+4+15+17、Contempt=AU14、Neutral=AU0。

**结论**：DESIGN.md §3.1 给出的「高兴=AU6+12」「惊讶=AU1+2+5+26」「悲伤=AU1+4+15」——悲伤与 Table 2 的两个可选分支之一吻合；高兴的 AU6 与惊讶的 AU26 在 Table 2 判定标准中都**没有依据**，更接近 Figure 1 插图示例或其他文献里的经典 EMFACS 原型组合（但示例用的是 AU25/27，不是 AU26）。详见「核对结论」。

---

## 附录 B · MediaPipe 52 个 blendshape 类别名（两种口径对照）

**口径一：BlendshapeV2 学术模型（arXiv:2309.05782 配套 Model Card 附录，代表 ARKit 风格的原始 52 项）**——52 项，**含 tongueOut，不含 _neutral**：

```
1 browDownLeft        14 mouthDimpleLeft     27 mouthClose         40 mouthRight
2 browDownRight       15 mouthDimpleRight    28 mouthDimpleLeft    41 mouthRollLower
3 browInnerUp         16 mouthFrownLeft      29 mouthDimpleRight   42 mouthRollUpper
4 browOuterUpLeft     17 mouthFrownRight     30 mouthFrownLeft     43 mouthShrugLower
5 browOuterUpRight    18 mouthFunnel         31 mouthFrownRight    44 mouthShrugUpper
6 cheekPuff           19 mouthLeft           32 mouthFunnel        45 mouthSmileLeft
7 cheekSquintLeft     20 mouthLowerDownLeft  33 mouthLeft          46 mouthSmileRight
8 cheekSquintRight    21 mouthLowerDownRight 34 mouthLowerDownLeft 47 mouthStretchLeft
9 eyeBlinkLeft        22 mouthPressLeft      35 mouthLowerDownRight 48 mouthStretchRight
10 eyeBlinkRight      23 mouthPressRight     36 mouthPressLeft     49 mouthUpperUpLeft
11 eyeLookDownLeft    24 mouthPucker         37 mouthPressRight    50 mouthUpperUpRight
12 eyeLookDownRight   25 mouthRight          38 mouthPucker        51 noseSneerLeft
13 eyeLookInLeft      26 mouthRollLower      39 mouthRollUpper     52 tongueOut
```
（完整原始顺序见模型卡 PDF 附录，此处按名称去重列出，顺序以模型卡为准，不逐一重排。）

**口径二：Face Landmarker Tasks API 实际输出（`face_blendshapes_graph.cc` 源码 `kBlendshapeNames` 数组，逐条核实）**——52 项，**含 _neutral，不含 tongueOut**：

```
_neutral, browDownLeft, browDownRight, browInnerUp, browOuterUpLeft, browOuterUpRight,
cheekPuff, cheekSquintLeft, cheekSquintRight, eyeBlinkLeft, eyeBlinkRight,
eyeLookDownLeft, eyeLookDownRight, eyeLookInLeft, eyeLookInRight, eyeLookOutLeft, eyeLookOutRight,
eyeLookUpLeft, eyeLookUpRight, eyeSquintLeft, eyeSquintRight, eyeWideLeft, eyeWideRight,
jawForward, jawLeft, jawOpen, jawRight, mouthClose, mouthDimpleLeft, mouthDimpleRight,
mouthFrownLeft, mouthFrownRight, mouthFunnel, mouthLeft, mouthLowerDownLeft, mouthLowerDownRight,
mouthPressLeft, mouthPressRight, mouthPucker, mouthRight, mouthRollLower, mouthRollUpper,
mouthShrugLower, mouthShrugUpper, mouthSmileLeft, mouthSmileRight, mouthStretchLeft, mouthStretchRight,
mouthUpperUpLeft, mouthUpperUpRight, noseSneerLeft, noseSneerRight
```

**核对结论**：两种口径去掉各自的「非表情基」条目（口径一去 tongueOut，口径二去 _neutral）后，剩下的 **51 个命名完全相同**。DESIGN.md §2「ARKit 表情基...共 52 个...去掉 tongueOut 余 51 个」的表述，对应的是口径一（学术模型/ARKit 原始命名）；如果实际调用的是 MediaPipe Tasks API（口径二），拿到的 52 项里第一项是 `_neutral` 而非 tongueOut，去掉 `_neutral` 同样得到这 51 个。**两条路径最终落到同一个 51 维命名集合，DESIGN.md 的技术结论成立，但「52 个」的构成需要按实际调用的 API 版本对齐说法**（建议在 §2 补一句：若走 Tasks API，52 项里的多余项是 `_neutral` 而非 `tongueOut`）。

许可证：MediaPipe 整体采用 **Apache License 2.0**（[LICENSE 文件](https://github.com/google-ai-edge/mediapipe/blob/master/LICENSE)），BlendshapeV2 模型卡同样标注 Apache 2.0。DESIGN.md §2 标注「Apache-2.0」核对无误。

---

## 附录 C · ARKit blendshape ↔ FACS AU 对照表（非官方，业内常用整理）

来源：[Melinda Ozel — ARKit to FACS: Blendshape Cheat Sheet](https://melindaozel.com/arkit-to-facs-cheat-sheet/)（FACS 从业者整理，自述曾与 FaceShift 前员工核对；非 Apple 官方文档，非同行评审文献，仅供工程参考）。

| ARKit blendshape | FACS AU |
| --- | --- |
| browInnerUp | AU1 |
| browOuterUp（L/R） | AU2 |
| browDown（L/R） | AU4 |
| eyeWide（L/R） | AU5 |
| cheekSquint（L/R） | AU6 |
| eyeSquint（L/R） | AU7 |
| mouthClose | AU8 |
| noseSneer（L/R） | AU9 |
| mouthUpperUp（L/R） | AU10 |
| mouthShrugUpper / mouthShrugLower | AU17 |
| mouthPucker | AU18 |
| mouthStretch（L/R） | AU20 |
| mouthFunnel | AU22 |
| mouthPress（L/R） | AU24 |
| jawOpen | AU26 / AU27（幅度越大越接近 27） |
| mouthRollUpper / mouthRollLower | AU28 |
| mouthSmileLeft / mouthSmileRight | AU12 |
| mouthDimple（L/R） | AU14 |
| mouthFrown（L/R） | AU15 |
| mouthLowerDown（L/R） | AU16 |
| eyeBlink（L/R） | AU45（眨眼，FACS 里单独编码，非 1–43 常规 AU） |

DESIGN.md §9「工程事实」举例的 browInnerUp≈AU1、mouthSmileLeft/Right≈AU12、cheekSquint≈AU6、noseSneer≈AU9、jawOpen≈AU26/27，与本表一致，核对无误。

---

## 核对结论

逐条列出「原文 → 问题 → 建议改法」：

*以下各条已据此修正 DESIGN.md（2026-09-28）。*

1. **原文**（§2）：「EMOCA（Danecek 等 CVPR 2022）直接从 3D 人脸表情系数回归情绪」
   **问题**：表述不够准确，但方向没错。EMOCA 的主任务是「图像 → 3D 表情系数」的重建，训练时用情绪识别网络的感知损失监督重建；论文另外在重建出的几何参数上做了情绪识别，摘要写明“our purely geometric approach is on par with the best image-based methods”（初稿核对时漏看了这一点，误判为「方向反了」，已经主窗口对照 arXiv 摘要更正）。
   **改法**：写明「在重建出的 3D 表情参数上做情绪识别，纯几何方法与最好的图像方法相当」。

2. **原文**（§3.1）：「惊讶 = AU1+AU2+AU5+AU26」，并署名依据「Lucey 等 CVPRW 2010」（即 CK+）
   **问题**：核对 CK+ 论文 Table 2 原文，Surprise 的判定标准是「AU1+2 或 AU5，且 AU5 强度不超过 B 级」，**完全没有提到 AU26**。论文 Figure 1 的示例图用的是 AU1+2+5+25+27，也不是 AU26。AU26（Jaw Drop）出现在惊讶表情里是很多其他 FACS 资料的常见说法，但不是 CK+ 这篇论文给出的判据。
   **建议改法**：要么去掉「Lucey 等 CVPRW 2010」这个出处标注，只留 EMFACS 一般性说法；要么把合成器配置改成严格对齐 CK+ Table 2 的「AU1+2 或 AU5（强度≤B）」，两者选一，不要把两个不同来源的组合混在一起还都算在 CK+ 名下。

3. **原文**（§3.1）：「高兴 = AU6+AU12」，同样署名「Lucey 等 CVPRW 2010」
   **问题**：CK+ Table 2 对 Happy 的判定标准只要求「AU12 必须出现」，**不要求 AU6**。AU6+12 是经典 FACS 文献里「杜兴微笑（Duchenne smile，真笑标志）」的原型组合，也出现在 CK+ Figure 1 的示例图（AU6+12+25）里，但不是 Table 2 本身的判定标准。
   **建议改法**：如果合成器就是想做「杜兴式高兴」（更真实、更适合作为摆拍先验），维持 AU6+12 没问题，但引用应写成「经典 EMFACS 高兴原型，CK+ Figure 1 亦以此为例」，而不是笼统地说「CK+ 情绪判定标准」。

4. **原文**（§3.3 数据集清单）：FER2013 与其余四个公开集（RAF-DB、AffectNet、CK+、KDEF）并列，统一标注「全部仅限非商用研究」
   **核对结果**：结论方向正确，但值得补充一句更准确的表述——FER2013 **不是** 像 RAF-DB/AffectNet 那样有一份「非商用科研许可协议」，而是压根**没有官方许可证**：图片经 Google 图片搜索抓取，原始版权归属不明，Kaggle 上各个二次上传版本自行标注的许可证不构成权利人授权。也就是说 FER2013 的风险其实比「非商用协议」更高（版权来源本身存疑），不是「不可商用」这一句能完全概括的。
   **建议改法**：在 §3.1/§3.3 给 FER2013 单独加一句「版权来源不明，非官方授权数据，风险高于其余几个持证非商用数据集」。

5. **未在 DESIGN.md 中明确写出、但会被读者用来对照的一处口径歧义**（§2「共 52 个...去掉 tongueOut 余 51 个」）：
   **问题**：不是错误，是口径未挑明。ARKit/BlendshapeV2 学术模型的 52 项里第 52 项是 tongueOut、没有 _neutral；而 MediaPipe Face Landmarker **Tasks API 实际返回**的 52 项里第 1 项是 `_neutral`、**没有 tongueOut**。两者去掉各自多出来的那一项后是同一组 51 个命名，所以最终结论不影响，但如果实现方直接对着 Tasks API 的输出数组去减 "tongueOut" 会找不到这一项。
   **建议改法**：在 §2 补一句「若通过 MediaPipe Face Landmarker Tasks API 获取，返回的 52 项里多出来的是 `_neutral` 而非 `tongueOut`；两种口径最终对齐到同一组 51 个命名」。

未发现其他实质性引用错误：Ekman/Friesen/Hager、Gorishniy（FT-Transformer）、Luo（ME-GraphAU）、Li（SRERL）、Wang（SCN / SCE）、Menon（logit adjustment）、Hendrycks（Outlier Exposure）、Guo（温度校准）、Liu（能量 OOD）、Zhang（mixup）、Geng（LDL）、Du/Tao/Martinez（复合表情）、Jack 等（东亚观察者依赖眼部区域）、Snell（原型网络）、Hinton（知识蒸馏）、POSTER++/DDAMFN/APViT、Sentis 版本与 opset 范围，作者、年份、会议/期刊均与官方来源一致。
