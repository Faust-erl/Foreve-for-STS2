# 参与贡献

感谢你愿意参与 Foreve 的开发。以下约定能让大家更顺畅地协作。

## 开发流程

1. Fork 本仓库。
2. Clone 到本地：

   ```powershell
   git clone https://github.com/<你的用户名>/Foreve.git
   cd Foreve
   ```

3. 创建功能分支：

   ```powershell
   git checkout -b feat/my-change
   ```

4. 参考 [BUILDING.md](BUILDING.md) 配置本地环境并验证修改。
5. Commit 并推送：

   ```powershell
   git add .
   git commit -m "feat(dual): 说明你改了什么"
   git push origin feat/my-change
   ```

6. 到 GitHub 发起 Pull Request。

## 分支 / Commit 约定

- 分支名：`feat/...`、`fix/...`、`chore/...`、`docs/...`
- Commit message 使用 conventional commits：

  ```text
  fix(combat): 修复卡牌伤害预览
  feat(silver-key): 新增银钥顺序效果
  chore(assets): 更新角色动画
  ```

## 代码约定

- C# 项目使用 .NET 9 / Godot 4.5.1。
- 新卡牌/遗物/药水/能力：创建对应 C# 类，并同步更新 `Foreve/Foreve/localization/` 中的中英文 JSON。
- 新美术资源放入 `Foreve/Foreve/Assets/`，并让资源路径与代码中的 `res://Foreve/Assets/...` 一致。
- 不要提交：
  - `foreve.pck`
  - `obj/`、`bin/`、`.godot/`
  - `.env`、`__pycache__/`
  - 游戏提取数据、第三方教程、未授权素材

## 本地化

- `localization/zhs/cards.json` 等按表拆分。
- 修改本地化 JSON 后通常只需 `dotnet build`，无需重打 PCK。

## 提交前检查

```powershell
git status
git ls-files | Select-String -Pattern '\.pck$|\.dll$|\.pdb$|\.env$'
```

确认没有意外加入构建产物或本地密钥。
