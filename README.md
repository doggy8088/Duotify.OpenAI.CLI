# OpenAI CLI

此應用程式是一個命令列介面 (CLI) 工具，用於與 OpenAI 相容的 API 互動。

它允許使用者傳送提示至各種 OpenAI 端點，包括聊天完成、模型清單、審核、影像產生和 Embeddings。

此工具會將對話記錄儲存在 JSON 檔案中，並依主題整理。

## 用途

### 必要條件

在執行應用程式之前，請確定已設定下列環境變數：

* `<PROVIDER>_API_ENDPOINT`：API 端點 URL (例如 `OPENAI_API_ENDPOINT=https://api.openai.com/v1`)。
* `<PROVIDER>_API_KEY`：您的 API 金鑰 (例如 `OPENAI_API_KEY=sk-xxxxxxxx`)。
* `<PROVIDER>_API_MODEL`：要使用的預設模型 (例如 `OPENAI_API_MODEL=gpt-4o`)。

您可以設定 `OPENAI_COMPATIBLE_PROVIDER` 來指定提供者 (預設為 `OPENAI`)。

資料會儲存在 `$OPENAI_DATA_DIR` (如果已設定)、`$XDG_CONFIG_HOME` (如果已設定) 或 `$HOME/.openai` 中。

### 基本語法

```bash
# 一般用法
dotnet run -- [-n] [-a api_name] [-o dump_file] [INPUT...]
dotnet run -- -i dumped_file

# 預設 API (chat/completions)
dotnet run -- [-c] [+property=value...] [@TOPIC] [-f file | prompt ...]

# 其他 API 範例
dotnet run -- -a models
dotnet run -- -a moderations [-f file | prompt ...]
dotnet run -- -a images/generations [-f file | prompt ...]
dotnet run -- -a embeddings [-f file | prompt ...]
```

**注意**： 使用 `dotnet run` 時，請在應用程式引數前加上 `--`，以將它們與 `dotnet` 指令本身的選項分開。

### 選項

* `-a <api_name>`：指定要呼叫的 API 端點 (預設：`chat/completions`)。範例：`models`、`moderations`、`images/generations`、`embeddings`。
* `-c`：在現有主題中繼續對話。需要 `@TOPIC` (除非主題是 `General` 且檔案存在)。
* `-f <file>`：從指定的檔案讀取提示。如果檔案名稱為 `-` 或未提供提示 / 檔案，則從標準輸入讀取。
* `-n`：試執行模式。顯示要求詳細資料，但不會實際呼叫 API。
* `-o <filename>`：將原始 API 回應傾印至檔案並結束 (串流不支援)。
* `-i <filename>`：使用先前傾印的檔案作為 API 回應，而不是發出新要求。
* `-h` 或 `--help`：顯示說明訊息。
* `+property=value`：覆寫 API 要求的預設承載屬性。範例：`+temperature=0.7`、`+stream=false`。
* `@TOPIC`：指定要使用或建立的對話主題。預設主題為 `General`。
* `prompt ...`：直接在命令列上提供的提示文字。

### 主題

* 主題可讓您將對話記錄分開。它們會儲存為 `$OPENAI_DATA_DIR` 中的 `<topic_name>.json` 檔案。
* 若要使用特定主題，請在提示之前包含 `@topic_name`。
* 若要建立新主題，請使用 `@new_topic` 並提供初始系統提示：

  ```bash
  dotnet run -- @my_project 你是一位樂於助人的程式設計助理。
  ```

* 使用 `-c` 選項可在現有主題中繼續對話。

### 範例

```bash
# 取得簡單的聊天完成
dotnet run -- 撰寫關於 C# 的笑話

# 建立新主題並開始聊天
dotnet run -- @my_story 你是一位故事寫手。 "從前從前..."
dotnet run -- -c @my_story "...有一位程式設計師。"

# 從檔案讀取提示
echo "將此文字翻譯成法文" > prompt.txt
dotnet run -- -f prompt.txt

# 使用不同的模型和溫度進行聊天完成
dotnet run -- +model=gpt-3.5-turbo +temperature=0.5 "解釋量子運算"

# 列出可用的模型
dotnet run -- -a models

# 產生影像
dotnet run -- -a images/generations "一隻太空貓"

# 檢查文字是否符合審核標準
dotnet run -- -a moderations "這是範例文字。"

# 試執行要求
dotnet run -- -n "測試提示"

# 將 API 回應儲存至檔案
dotnet run -- -o response.json "什麼是 REST API？"

# 使用儲存的回應
dotnet run -- -i response.json

# 關閉 stream 回應
dotnet run -- +stream=false "Describe Taiwan. Answer me in zh-tw."
```
