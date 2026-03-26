# OpenClaw Middleware

OpenClaw 中间件服务 - 为客户端提供安全加密通信层，代理转发到 OpenClaw Gateway。

## 功能特性

- 🔐 **端到端加密** - AES-256-GCM 消息加密 + RSA-2048 密钥交换
- 🔑 **多客户端支持** - 独立客户端令牌管理
- 📁 **文件传输** - 分块上传，支持图片/文档
- 💓 **心跳检测** - 自动清理空闲连接
- 🛡️ **速率限制** - 防止滥用和 DDoS
- 📊 **结构化日志** - Serilog 日志输出

## 技术栈

- .NET 8
- ASP.NET Core Minimal APIs
- WebSocket (System.Net.WebSockets)
- Serilog

## 快速开始

### 构建

```bash
dotnet restore
dotnet build --configuration Release
```

### 运行

```bash
# 开发环境
dotnet run --project src/OpenClawMiddleware

# 生产环境发布
dotnet publish -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -o ./publish

# 运行发布版本
./publish/OpenClawMiddleware
```

### 配置

编辑 `appsettings.json` 或设置环境变量：

```bash
export GATEWAY_TOKEN="your-gateway-token"
export ASPNETCORE_ENVIRONMENT=Production
```

## 客户端令牌管理

### 创建客户端

```bash
./OpenClawMiddleware create-client --name "Desktop Client"
```

### 列出客户端

```bash
./OpenClawMiddleware list-clients
```

### 禁用客户端

```bash
./OpenClawMiddleware disable-client --id <client-uuid>
```

## 通信协议

### 认证流程

1. 客户端建立 WebSocket 连接
2. 发送 `auth` 消息（包含 clientId 和 token）
3. 服务端验证令牌，返回会话密钥（RSA 加密）
4. 后续消息使用 AES-GCM 加密

### 消息格式

```json
{
  "type": "chat|auth|heartbeat|file_upload_request",
  "messageId": "uuid-string",
  "timestamp": 1711425600000,
  "nonce": "base64-random-12bytes",
  "payload": "base64-encrypted-data",
  "tag": "base64-gcm-auth-tag"
}
```

## 部署

### systemd 服务

```bash
sudo cp deploy/middleware.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable openclaw-middleware
sudo systemctl start openclaw-middleware
```

### 日志查看

```bash
journalctl -u openclaw-middleware -f
# 或
tail -f /var/log/openclaw/middleware.log
```

## 开发

### 运行测试

```bash
dotnet test --verbosity normal
```

### 代码覆盖率

```bash
dotnet test --collect:"XPlat Code Coverage"
```

## 目录结构

```
OpenClawMiddleware/
├── src/
│   └── OpenClawMiddleware/
│       ├── Program.cs
│       ├── Services/          # 核心服务
│       ├── Handlers/          # 消息处理器
│       ├── Middleware/        # ASP.NET 中间件
│       └── Models/            # 数据模型
├── tests/
│   └── OpenClawMiddleware.Tests/
└── deploy/                    # 部署脚本
```

## 许可证

MIT
