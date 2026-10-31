# DeepSeek AI 联想

AI 是可选工具；默认不需要配置，也不在启动、学习、切换词库或打开编辑器时发送请求。

## 使用

- 首页直接点击“AI 联想 · DeepSeek”，或打开“菜单 → AI 联想设置”，填写自己的 API Key，默认模型为 `deepseek-flash`，也可选择 `deepseek-v4-pro`。
- “测试连接”使用当前输入查询模型列表，不生成文字、不自动保存设置。
- 学习词卡和完成页的“逐词复习”均提供“我的联想”，可手写，也可点击“AI 生成”。生成后内容仍是草稿，可修改、换一种，或撤回到上一次生成之前。
- 生成期间可取消请求或关闭窗口。原有内容不变；迟到的响应也不会覆盖它。
- 点击“保存”才调用原来的事务式保存逻辑，写入当前词库的个人联想；关闭或取消不保存。
- 生成失败、超时、超出长度或接口返回格式不正确时，不替换已有草稿，不自动重试。
- 已保存的内容与手写联想采用同样的离线存储和按词库隔离方式。
- 逐词复习直接显示当前词的联想；保存后立即更新，翻页、返回或重启后仍保留。清空并保存恢复词库自带联想，没有自带联想时不显示空段落。修改联想不会重算分数、历史答案或复习日期。

## 内置风格

允许夸张、搞笑、荒诞、离奇、出人意料，强调具体画面、动作、冲突或反转。尽量从读音、词形或熟悉的词出发，与给定词义形成清楚的联系；故事可以不现实，但内部逻辑要成立。

突出一个鲜明记忆点，避免无关情节。想象中的拆词不得声称是真实词根、词源；谐音应接近实际发音。只输出联想，80 字以内，不设最低字数，不附标题、解释或思考过程。

提示词源码在 `src/Cet6WordMachine/AiHintPrompt.cs`。界面不会限制手写联想的长度，80 字上限仅检查 AI 返回结果，按 Unicode 文本元素计数，包含标点和英文字母。超长结果不会被截断后默默保存。

## 网络与密钥

- 固定 HTTPS 官方接口 `https://api.deepseek.com/`，关闭自动重定向，不允许配置任意第三方转发地址。
- 生成使用 `POST /chat/completions`，非思考模式、非流式响应，最大输出 384 tokens，每次请求超时 45 秒。
- 业务输入仅含当前 `word`、`phonetic`、`meaning`，另有内置系统提示词。API Key 通过认证请求头发送给 DeepSeek。
- 不发送旧联想、例句、学习记录、个人答案或全部词库；不记录 Key 或展示原始服务端错误正文。
- 查询连接使用 `GET /models`，确认所选模型可用。没有定时联网、批量请求和后台生成。
- 请求费用取决于 DeepSeek 的实际计费；取消本地等待不保证撤销服务端已开始处理的请求或计费。
- Key 存在 `%LOCALAPPDATA%\WordMachineCommunity\ai\deepseek-settings.dat`，以 Windows 当前用户 DPAPI 加密。所有本机社区版实例共用该 Windows 用户的 AI 设置，与便携学习目录分开。
- 配置使用临时文件和替换方式保存；写入失败保留旧配置。取消设置窗口不覆盖无法读取的配置。
- 清空 Key 并保存即可删除该文件。迁移到不同 Windows 账号或电脑后重新填写，不把它当作可分享的便携配置。
- 发布源码或分享学习数据时不要附带自己的 API 配置；Git 已忽略 `deepseek-settings.dat*`。软件不内置共享 Key。

## 验证范围

自动测试用假 Key、模拟 HTTP 响应及独立账号覆盖正常、短文本、80 字边界、超长、空结果、异常 JSON、服务端错误、超时、取消、关闭、保存、撤回和无密钥手动使用。真实 Windows DPAPI 的加密往返和文件占用失败也经过验证。

没有提供真实 API Key，因此未进行付费生成，也不声称已验证模型实际输出质量。首次使用可先测试连接，再对一个单词生成并检查联想是否符合词义。

接口模型名称及请求参数依据 2026-10-31 读取的官方公开文档核对：

- [DeepSeek 模型与价格](https://api-docs.deepseek.com/zh-cn/quick_start/pricing)
- [DeepSeek Chat Completion](https://api-docs.deepseek.com/api/create-chat-completion)
- [DeepSeek 模型列表](https://api-docs.deepseek.com/api/list-models)
- [Windows CryptProtectData](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)
