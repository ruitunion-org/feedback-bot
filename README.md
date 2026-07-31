# Feedback Bot

[![build-and-test](https://github.com/ruitunion-org/feedback-bot/actions/workflows/build.yml/badge.svg)](https://github.com/ruitunion-org/feedback-bot/actions/workflows/build.yml)

A free and open-source Telegram Bot that allows you to anonymously
chat with multiple users in one Telegram Chat.

When a new user interacts with the Feedback Bot by sending a message,
the bot forwards the message to a specific topic dedicated to that user
within the Feedback Chat. If a topic for the user does not already
exist, the bot creates a new one. This ensures that each user's
message is organized in its own topic, allowing for clear and efficient
interaction.

Created and supported by [Russian IT Union](https://ruitunion.org/en/about/).

## 📝 Prerequisites

1. Telegram Bot Token;
2. Telegram Chat ID;
3. PostgreSQL 18 (automatically deployed via Docker Compose).

## 🛠️ Prepare the Environment

### Telegram bot

1. Create a bot with [@BotFather](https://t.me/BotFather);
2. After successfully creating the bot,
you will receive a **Telegram Bot Token**.

### Feedback Chat

1. Create a private chat;
2. Enable topics;
3. Get the **Telegram Chat ID**;

> You can get the ID of any user / chat / bot using desktop Telegram client.
To do that go to *Settings* / *Advanced* / *Experimental settings* and
enable *Show peer ID in Profile*. Now you will be able to see all the IDs.

![how_to_get_id](./docs/image01.png)

4. Add the bot from the previous step and make it Administrator;
5. The only permission required for the bot is to manage topics.

![required_permissions](./docs/image02.png)

## 🚀 Run

### From GitHub Container Registry

1. Download `docker-compose.yml` & `feedback_bot.env` files into one folder;
2. Edit `feedback_bot.env` with any text editor and replace these values:
    - `<bot_token>` with **Telegram Bot Token**,
    - `<chat_id>` with **Telegram Chat ID**,
    - `<start_text>` with your greeting message your new users;
3. Run the following command:

```sh
docker compose up -d
```

This will start three services:
- **feedback_bot** — the bot itself
- **database** — PostgreSQL 18
- **dashboard** — .NET Aspire Dashboard (monitoring, metrics, traces)

### From source code

1. Clone this repo;
2. Edit `feedback_bot.env` with any text editor and replace these values:
    - `<bot_token>` with **Telegram Bot Token**,
    - `<chat_id>` with **Telegram Chat ID**,
    - `<start_text>` with your greeting message your new users;
3. Edit `docker-compose.yml`: in `feedback_bot` section change `image`
value to `ghcr.io/ruitunion-org/feedback-bot:local`
3. Run the following commands:

```sh
docker build -t ghcr.io/ruitunion-org/feedback-bot:local -f ./RuItUnion.FeedbackBot/Dockerfile .
docker compose up -d
```

## ⚙️ Configuration

### Environment variables

| Variable | Description | Required |
|----------|-------------|----------|
| `ConnectionStrings__Telegram` | Telegram Bot Token | Yes |
| `AppOptions__FeedbackChatId` | Feedback Chat ID | Yes |
| `AppOptions__Start` | Welcome message for `/start` command | Yes |
| `ConnectionStrings__RuItUnion-FeedbackBot-Database` | PostgreSQL connection string | Yes |
| `Migrator__UpdateDatabase` | Auto-apply EF Core migrations (`true`/`false`, default `true`) | No |
| `AppOptions__OverrideCultureTag` | Force locale (e.g. `ru-RU`) | No |
| `AppOptions__FeedbackBotToken` | Alternative way to provide bot token | No |
| `ALL_PROXY` | SOCKS5 proxy (e.g. `socks5://user:pass@host:port`) | No |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OpenTelemetry OTLP endpoint | No |
| `OTEL_SERVICE_NAME` | OpenTelemetry service name | No |
| `HTTP_PORTS` | HTTP ports (default `8080`) | No |

### Feature flags

Feature flags are configured in `appsettings.json` under `feature_management`:

| Flag | Default | Description |
|------|---------|-------------|
| `ChinaSpamFilter` | `true` | Detects Chinese spam by CJK character ratio (>15%) |
| `UaPropagandaFilter` | `true` | Detects Ukrainian propaganda by regex patterns |
| `CsvReports` | `true` | Enables `/TopicCsv` command for CSV reports |
| `ForceCultureSet` | `false` | Forces a specific locale via `AppOptions__OverrideCultureTag` |
| `LegacyCommands` | `false` | Enables legacy command aliases (`/addadmin`, `/closetask`, `/settaskname`) |
| `DoNotAllowEmptyHeader` | `false` | Rejects topics with empty header |
| `EnableMigratorFromV01` | `false` | Enables data migration from v0.1 schema |

## 🌟 Features

### Commands

Commands other than `/start` are only available in group chat.
Use `/help` to get information about all commands.

#### General

- `/start` — Starts the bot and displays a welcome message.
- `/help` — Displays a list of all commands with their descriptions.

#### Topic Management

- `/delete` — Removes a reply in the user chat.
- `/open` — Opens a topic in the feedback chat.
- `/close` — Closes a topic in the feedback chat.
- `/link <id>` — Replies with a link to the last message of a topic.
- `/forceSend <text>` — Force-sends a message to the user.
- `/TopicList` — List all open topics with links.

#### Tags

- `/tag` — View tags of the current topic.
- `/tag <key> <value>` — Set a tag on the current topic.
- `/tagRemove <key>` — Remove a tag from the current topic.
- `/tag <id> <key> <value>` — Set a tag on a topic by its number.
- `/tagList` — List all tags with usage counts.

Special tag keys:
- `header` — Sets the topic header/description (displayed in forum title)
- `type` — Request type
- `result` — Result (`true`/`false`, `success`/`fail`, `1`/`0`)

Shorthand syntax: `#key value` (no spaces in key).

#### Reports

- `/TopicCsv` — CSV report of all topics.
- `/TopicCsv <date>` — CSV report filtered by minimum date.
- `/TopicCsv <start_date> <end_date>` — CSV report for a date range.

#### Moderation

- `/ban` — Bans the user.
- `/unban` — Unbans the user.

#### Spam Management

- `/spamInfo <id>` — Shows user info and spam message text.
- `/spamDownload <id>` — Downloads full spam info as JSON.
- `/spamRestore <id>` — Restores a spam message and creates a topic.

### Permissions

| Command        | Bot User | Chat User | Chat Admin | Service Admin |
|----------------|----------|-----------|------------|---------------|
| `/help`        | ✅        | ✅         | ✅          | ✅             |
| `/start`       | ✅        | ✅         | ✅          | ✅             |
| `/delete`      | ❌        | ✅         | ✅          | ✅             |
| `/open`        | ❌        | ❌         | ✅          | ✅             |
| `/close`       | ❌        | ❌         | ✅          | ✅             |
| `/ban`         | ❌        | ❌         | ✅          | ✅             |
| `/unban`       | ❌        | ❌         | ✅          | ✅             |
| `/tag`         | ❌        | ❌         | ✅          | ✅             |
| `/tagRemove`   | ❌        | ❌         | ✅          | ✅             |
| `/list`        | ❌        | ❌         | ✅          | ✅             |
| `/tagList`     | ❌        | ❌         | ✅          | ✅             |
| `/csv`         | ❌        | ❌         | ✅          | ✅             |
| `/link`        | ❌        | ❌         | ✅          | ✅             |
| `/forceSend`   | ❌        | ❌         | ✅          | ✅             |
| `/sync`        | ❌        | ❌         | ❌          | ✅             |
| `/spamInfo`    | ❌        | ❌         | ✅          | ✅             |
| `/spamDownload`| ❌        | ❌         | ✅          | ✅             |
| `/spamRestore` | ❌        | ❌         | ✅          | ✅             |

### Anti-Spam System

Two spam filters are available (enabled via feature flags):

- **ChinaSpamFilter** — detects Chinese spam by CJK character ratio (>15%).
- **UaPropagandaFilter** — detects Ukrainian propaganda by regex patterns.

When spam is detected: the user is banned, the message is saved to the database,
and admins receive a notification with the spam ID.

### Message Editing

Operators can edit messages in the feedback chat — changes are automatically
synced to the user.

### Auto Role Sync

Chat administrators of the feedback chat automatically receive the `admin`
role in the bot upon sending a command.

### Observability

The bot exposes HTTP endpoints:
- `/health` — health check endpoint (includes Telegram API check)
- `/alive` — liveness probe
- `/metrics` — Prometheus metrics endpoint

### Rate Limiting

Command rate limit: 1 request per second (configurable in `RateLimit` section).

### Proxy Support

SOCKS5 proxy is supported via the `ALL_PROXY` environment variable.

## 🏗️ Architecture

The project consists of six sub-projects:

| Project | Purpose |
|---------|---------|
| `RuItUnion.FeedbackBot` | Main bot application (ASP.NET Core) |
| `RuItUnion.FeedbackBot.AppHost` | .NET Aspire AppHost for orchestration |
| `RuItUnion.FeedbackBot.Data` | Data layer (EF Core, PostgreSQL) |
| `RuItUnion.FeedbackBot.Data.Old` | Data migration from v0.1 |
| `RuItUnion.FeedbackBot.ServiceDefaults` | Shared .NET Aspire services (OpenTelemetry, health checks) |
| `RuItUnion.FeedbackBot.Tests` | Unit tests (xUnit) |

## 🤝 Contributing

Contributions are welcome. Here are some ways you can help:

1. **Report bugs**: If you find a bug, please report it by creating an
issue on GitHub;
2. **Request features**: Have an idea for a new feature?
Let us know by creating a feature request;
3. **Submit pull requests**: If you'd like to fix a bug or add a feature,
feel free to fork the repository and submit a pull request.
