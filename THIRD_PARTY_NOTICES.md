# 第三方来源与授权说明

本项目代码采用 GPL-3.0-only；本文件同时说明词库素材，不能用代码许可证替代第三方的原有声明。

## ECDICT

- 仓库：https://github.com/skywind3000/ECDICT
- 使用字段：词头、中文释义、音标、考试标签、屈折形式。
- 上游声明：MIT，原文随附于 `licenses/ECDICT-MIT.txt`，保留其版权署名。
- 词典快照 SHA256 及 Git blob 在 `data/sources/provenance.json`。
- `data/sources/lexicon.json` 为与本项目四六级范围相关的字段子集；没有复制其全部词典和附带素材。

## 高中及高考排除依据

- 《普通高中英语课程标准（2017 年版 2020 年修订）》：人民教育出版社公开原件。
- 《普通高中英语课程标准（2017 年版 2025 年修订）》：所核对的公开 PDF 镜像；不称该镜像为官方发布站点。
- Qwerty Learner：https://github.com/RealKai42/qwerty-learner 。采用 27 份高中/高考词表的词头作为补充排除依据，未复制其音频、例句、释义或界面。
- Qwerty Learner 上游许可为 GPL v3，原文保留于 `licenses/Qwerty-LICENSE.txt`；本项目按 GPL v3 整理发行。
- 本仓库分发可复核的词头排除集合和来源元数据，不分发教材或课标 PDF 全文。人工基础词形判断与官方课标证据分别列出。

## 真题阅读样本

- 来源索引：https://english-exam.lazynote.cn/cet4/ 和 https://english-exam.lazynote.cn/cet6/ 。
- 本项目提取词形出现统计，保留网页 URL、哈希、考期列表和统计规则。
- 不随仓库分发真题文章、完整网页、答案解析或长段原文；例句和联想字段未从这些网页复制。
- 来源网站、教育考试机构与本项目无隶属或背书关系。

## 用户导入

用户词库和学习记录保存在本机，不随项目分发。分享或公开自己的词库时，应同时保留其来源和相应授权说明。
