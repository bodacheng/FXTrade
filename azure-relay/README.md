# Azure Functions 中转部署

该服务把 Twelve Data 和 OpenAI 的供应商密钥留在 Azure。Unity 客户端只请求 Function App 的 HTTPS 地址，不保存也不发送供应商密钥。

## 接口

- `GET /api/health`
- `GET /api/market/quote?symbol=USD%2FJPY`
- `GET /api/market/candles?symbol=USD%2FJPY&interval=5min&outputSize=160`
- `POST /api/advice`，正文为 `{"prompt":"...","mode":"conservative"}` 或 `forced_directional`

## 本地验证

需要 Node.js 22、Azure Functions Core Tools v4 和 Azurite。

```bash
cd azure-relay
npm install
cp local.settings.example.json local.settings.json
npm test
npm start
```

本地服务启动后，可将 Unity 的 `AZURE_RELAY_BASE_URL` 环境变量设为 `http://localhost:7071`。

## 创建 Azure 资源

Flex Consumption 是当前推荐的无服务器托管方案。以下示例使用日本东部区域，资源名需要全局唯一：

```bash
export RESOURCE_GROUP=testfxtrade-rg
export LOCATION=japaneast
export STORAGE_NAME=testfxtradestorage
export FUNCTION_APP=testfxtrade-relay

az login
az group create --name "$RESOURCE_GROUP" --location "$LOCATION"
az storage account create \
  --name "$STORAGE_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --sku Standard_LRS
az functionapp create \
  --resource-group "$RESOURCE_GROUP" \
  --name "$FUNCTION_APP" \
  --storage "$STORAGE_NAME" \
  --flexconsumption-location "$LOCATION" \
  --runtime node \
  --runtime-version 22
```

在 Azure Portal 的 Function App > Settings > Environment variables 中添加：

- `TWELVE_DATA_API_KEY`
- `OPENAI_API_KEY`
- `OPENAI_MODEL`，默认可设为 `gpt-5.6`

正式环境建议把前两个值放入 Azure Key Vault，并在 Function App Settings 中使用 Key Vault reference，不要把真实值写进命令、仓库或部署包。

## 部署

```bash
cd azure-relay
npm ci
npm test
func azure functionapp publish "$FUNCTION_APP"
curl "https://$FUNCTION_APP.azurewebsites.net/api/health"
```

健康检查只返回两个供应商是否已配置，不会返回密钥内容。

部署成功后，将 `Assets/Resources/AzureRelayConfig.json` 的 `baseUrl` 改为：

```text
https://<FUNCTION_APP>.azurewebsites.net
```

## 上线前保护

这些示例接口使用匿名访问，目的是避免在手机包里再嵌入一枚可提取的 Function key。公开发行前仍需在入口层增加 Azure API Management 限流、预算告警和真实用户身份验证；仅隐藏 Function URL 不能防止配额被滥用。
