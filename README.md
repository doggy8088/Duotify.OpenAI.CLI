# Duotify.OpenAI.CLI

此應用程式是一個跨平台的 CLI 命令列工具，用於與 OpenAI 相容的 API 互動。

它允許使用者傳送提示至各種與 OpenAI API 相容的端點，包括 Chat Completion, models, moderations, images/generations 和 embeddings 等等。

## 安裝

1. 確保已安裝 [.NET 8.0](https://dotnet.microsoft.com/zh-tw/download?WT.mc_id=DT-MVP-4015686) 或更新版本。

    安裝 .NET Runtime 或 .NET SDK 都可以。

2. 透過以下命令安裝本工具

    ```sh
    dotnet tool install --global Duotify.OpenAI.CLI
    ```

3. 安裝好之後，你可以透過 `openai-cli -h` 查詢用法。

## 必要條件

在執行應用程式之前，請確定已設定下列環境變數：

* `<PROVIDER>_API_KEY`：您的 API 金鑰 (例如 `OPENAI_API_KEY=sk-xxxxxxxx`)

    無預設值。

* `<PROVIDER>_API_MODEL`：要使用的預設模型 (例如 `OPENAI_API_MODEL=gpt-4o`)

    預設值為 `gpt-4o`

* `<PROVIDER>_API_ENDPOINT`：API 端點 URL (例如 `OPENAI_API_ENDPOINT=https://api.openai.com/v1`)

    預設值為 `https://api.openai.com/v1`

> 您可以設定 `OPENAI_COMPATIBLE_PROVIDER` 來指定 AI 提供者 (`<PROVIDER>`) (預設為 `OPENAI`)。

⚠ 你必須最少設定 `OPENAI_API_KEY` 才能使用。

聊天主題 (`TOPIC`) 資料會儲存在 `$OPENAI_DATA_DIR` (如果已設定)、`$XDG_CONFIG_HOME` (如果已設定) 或 `$HOME/.openai` 中。

## 基本用法

```bash
# 一般用法
openai-cli [-n] [-a api_name] [-o dump_file] [INPUT...]
openai-cli -i dumped_file

# 預設 API (chat/completions)
openai-cli [-c] [+property=value...] [@TOPIC] [-f file | prompt ...]

# 其他 API 範例
openai-cli -a models
openai-cli -a embeddings [-f file | prompt ...]
openai-cli -a moderations [-f file | prompt ...]
openai-cli -a images/generations [-f file | prompt ...]
```

## 選項

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

## 主題聊天

* 主題可讓您將對話記錄分開。它們會儲存為 `$OPENAI_DATA_DIR` 中的 `<topic_name>.json` 檔案。
* 若要使用特定主題，請在提示之前包含 `@topic_name`。
* 若要建立新主題，請使用 `@new_topic` 並提供初始系統提示：

  ```bash
  openai-cli @my_project 你是一位樂於助人的程式設計助理。
  ```

  如果在 PowerShell 必須這樣寫：

  ```bash
  openai-cli '@my_project' 你是一位樂於助人的程式設計助理。
  ```

* 使用 `-c` 選項可在現有主題中繼續對話。

  ```bash
  openai-cli '@my_project' -c 你是一位樂於助人的程式設計助理。
  ```

## 更多使用範例

```bash
# 取得簡單的聊天完成
openai-cli 撰寫關於 C# 的笑話

# 建立新主題並開始聊天
openai-cli @my_story 你是一位故事寫手。 "從前從前..."
openai-cli @my_story -c "...有一位程式設計師。"

# 從檔案讀取提示
echo "將此文字翻譯成日文" > prompt.txt
echo "This is a book" >> prompt.txt
openai-cli -f prompt.txt

# 使用不同的模型和溫度進行聊天完成
openai-cli +model=gpt-3.5-turbo +temperature=0.5 "解釋量子運算"

# 列出可用的模型
openai-cli -a models

# 產生影像
openai-cli -a images/generations "一隻太空貓"

# 檢查文字是否符合審核標準
openai-cli -a moderations "這是範例文字。"

# 試執行要求
openai-cli -n "測試提示"

# 將 API 回應儲存至檔案
openai-cli -o response.json "什麼是 REST API？"

# 使用儲存的回應
openai-cli -i response.json

# 關閉 stream 回應
openai-cli +stream=false "Describe Taiwan. Answer me in zh-tw."

# 建立一個新主題 (TOPIC)
openai-cli '@translate' 'You are a professional translation assistant. Always translate any input text to Traditional Chinese (zh-tw). Maintain the original meaning and context while providing natural and fluent translations. If the input is already in Traditional Chinese, verify its accuracy and make improvements if necessary. Do not add explanations unless specifically requested.'

# 使用既有的主題進行對話
openai-cli '@translate' 'Hello, how are you?'

# 建立一個主題，接續主題說話，維持上下文
# Create a new topic and start conversation
openai-cli '@my' 'You are a helpful assistant who can understand and remember our conversations. Please respond in a friendly and professional manner. When needed, you will refer to previous conversations to provide better assistance. Speak in Traditional Chinese (zh-tw).'

openai-cli '@my' -c 'My name is Will.'
openai-cli '@my' -c '很多人也叫我「保哥」'
openai-cli '@my' -c '我住在 Taiwan, Taipei'
openai-cli '@my' -c '我喜歡透過 AI 解決各種問題'

# 透過 Pipe 傳文字時，在 PowerShell 可能會需要處理中文編碼問題
[Console]::InputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
echo "將此文字翻譯成日文: Good morning!" | openai-cli
```

## 開發時常用命令

```sh
# 注意： 使用 dotnet run 時，請在應用程式引數前加上 --，以將它們與 dotnet 指令本身的選項分開。
dotnet run 撰寫關於 C# 的笑話
dotnet run -- -a models -n
dotnet run -- -a models


# 封裝 NuGet 套件
dotnet pack -c Release

# 安裝 openai-cli 工具
dotnet tool install --global --add-source ./nupkgs openai-cli

# 更新 openai-cli 工具
dotnet tool update --global --add-source ./nupkgs openai-cli

# 卸載 openai-cli 工具
dotnet tool uninstall --global openai-cli
```
