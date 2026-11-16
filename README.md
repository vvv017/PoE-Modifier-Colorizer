# PoE 詞綴上色器 / PoE Modifier Colorizer

Windows x64 的 PoE 1 獨立客戶端英文詞綴編輯器。使用 .NET 8 Windows
Desktop Runtime。程式資料夾必須可寫入，請勿放入遊戲目錄。

**目前為開發版本。** 已通過本地編譯與資料匯入檢查，但桌面啟動曾出現
「已開啟」提示及主視窗「沒有回應」，正常互動尚未完成驗收。
現有單實例處理會嘗試喚回原視窗，Windows 拒絕切換前景時閃爍工作列；
仍需在實際桌面確認啟動與操作。詳見 [驗證紀錄](HANDOFF.md)。

此儲存庫提供原始碼，不包含遊戲資料、個人設定、撤銷紀錄或原生 codec。

## 使用

1. 開啟 `GgpkColorizer.exe`。平時只載入 `data/` 的本機清單、配色與比對結果。
2. 首次使用或每賽季按「更新本季詞綴清單」。同季遊戲更新後也可重按。
   這會唯讀匯入所選 `Content.ggpk`，保存完成後再與 PoEDB 比對。
3. 以分類或英文關鍵字找詞綴。按 Ctrl / Shift 可選多項，選色後按
   「設定選取詞綴」保存配色。此步不修改遊戲。
   選「紅藍王祭壇」後，可再篩選紅／藍王、玩家／異界小怪／頭目與正面／負面。
4. 關閉遊戲與啟動器，再按「套用選取項目」或「套用所有已設定項目」。
   首次請只套用一條，進遊戲確認顏色。預覽是近似色，不代表遊戲已驗證。
5. 在「改動紀錄」撤銷選取紀錄或最近一批。同一詞綴先撤銷最近一次改色。

清除保存的配色不會自動還原遊戲文字；還原已套用的修改請使用撤銷。
介面可切換繁體中文與英文，遊戲文字維持英文。

## 清單與比對

- 指定讀取地圖、祭壇描述檔及其 include，沒有全封包文字搜尋。
  祭壇另讀本地 AtlasPrimordialAltarChoices、ChoiceTypes、Mods、Stats 資料表，
  依 Stat ID 與數值條件找回共用文字，包含小怪掉落、玩家懲罰及怪物增強。
  共用檔只保留 `map_*` 或祭壇實際引用的描述，避免混入所有技能、裝備文字。
  清單仍可能包含歷史或隱藏詞條，不等於當季可隨機出現的詞綴名單。
- 紅藍王、玩家／異界小怪／頭目、正面／負面來自本地祭壇選項及 Mod ID。
  同一文字可有多種用途，也可同時屬於地圖和祭壇；篩選不會建立重複寫入目標。
  有些舊版或 Ruthless 詞條仍在遊戲資料中，來源提示會保留這項限制。
- 共用描述也可能在其他場景顯示；來源檔案和 Stat ID 可在預覽中查看。
- PoEDB 是提示來源。本地詞條才是編輯依據，差異或網路失敗不阻止操作。
- 網站候選依相同陣營、對象與正負效果匹配。文字模板相符不代表網站 Mod ID
  或數值範圍已核對；網站缺少陣營的列會略過並提示，不用文字猜陣營。
  無法可靠對應時明確標示候選、無法對應或未完成。
- 配色按穩定識別碼保留。更新後消失的詞條設定仍留在 `settings.json`；
  只有目前清單內可定位的項目能套用。

## 資料與撤銷

`data/settings.json` 保存路徑、語言與配色；`catalog.json` 保存本機清單；
`comparison.json` 保存綁定清單版本的 PoEDB 報告；`changes.json` 保存改動紀錄，
另保留一份小型 `changes.json.bak`。若 JSON 損壞，程式會停止載入並指出檔案位置；
不會自動把舊紀錄當作完整現況。這份 JSON 副本不是 GGPK 備份。
搬移程式時一起保留整個 `data` 資料夾，否則會失去配色與撤銷依據。

程式不建立 GGPK 備份或工作副本。寫入前保存每項精確原文、新文、定位資料與
資源雜湊，再寫入、保存索引並重開讀回。只修改選定的英文片段，保留其他語言、
BOM、換行與未選文字。每個詞條的數值占位符必須保持一致。

撤銷會先核對目前文字；遇到更新或其他工具造成的衝突就略過，不用舊文字覆蓋。
未完成的寫入紀錄會在下次手動匯入、套用或撤銷時核對，不在啟動時打開 GGPK。
撤銷只恢復詞條內容，不縮小封包或恢復原始二進位排列。中斷寫入仍可能損壞封包，
此時可能需要官方客戶端修復；逐項紀錄無法代替完整封包復原。

## 建置

使用 .NET SDK 8：

```powershell
dotnet restore GgpkColorizer.csproj --locked-mode
dotnet build GgpkColorizer.csproj -c Release --no-restore
dotnet publish GgpkColorizer.csproj -c Release --no-restore --self-contained false -o publish
```

將相容的 x64 `oo2core.dll` 放在 `publish/GgpkColorizer.exe` 旁。
本地驗證使用的 codec 來自
[LibGGPK3 v2.7.5 工具包](https://github.com/aianlinb/LibGGPK3/releases/tag/v2.7.5)。
codec 必須另行取得並遵守其授權，本儲存庫不提供該二進位檔。

## 授權

Copyright © 2026 vvv017。
本專案以 [GNU AGPL-3.0-or-later](LICENSE) 提供，沒有任何保證。
第三方元件及資料格式來源的授權保留於 `licenses/`。

## 第三方來源

- LibBundledGGPK3、LibBundle3、LibGGPK3 2.7.5：
  [來源與授權](https://github.com/aianlinb/LibGGPK3/tree/f2e1a39b3f99b4c1f35a6dd22754d43cc58d208c)，
  Copyright © 2022–2026 aianlinb，AGPL-3.0-or-later。
- SystemExtensions 1.8.0：
  [來源與授權](https://github.com/aianlinb/SystemExtensions/tree/1f5e58edd5da4810d60cd7dfacbb62c8aaab0f4f)，
  Copyright © 2024 aianlinb，MIT。完整授權文字在 `licenses/`。
- [PoEDB Modifiers](https://poedb.tw/us/Modifiers) 與
  [Eldritch Altar](https://poedb.tw/us/Eldritch_Altar) 提供比對參考。
- 本地 DAT 欄位對照採用
  [dat-schema](https://github.com/poe-tool-dev/dat-schema/tree/9f095ca68ae0c822e4b0d8be3525e0466ddec11d)
  的祭壇、Mods 和 Stats 定義，Copyright © 2021 Alexander Drozdov，MIT。
  實際欄位寬度、指標和參照在讀取時驗證。
  未支援的新結構會停止匯入，保留上一份清單與配色。

修改客戶端資料涉及 [GGG 使用條款](https://www.pathofexile.com/legal/terms-of-use-and-privacy-policy)
的限制，不能保證沒有帳號處分風險。
