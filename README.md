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
2. Telegram Chat ID.

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

## 🌟 Features

### Commands

Commands other than `/start` are only available in group chat.
Use `/help` to get information about all commands.

- `/start` - Starts the bot and displays a welcome message.
- `/help` - Displays a list of all commands with their descriptions.
- `/delete` - Removes a reply in the user chat.
- `/open` - Opens a topic in the feedback chat.
- `/close` - Closes a topic in the feedback chat.
- `/ban` - Bans the user.
- `/unban` - Unbans the user.

### Permissions

| Command   | Bot User | Chat User | Chat Admin |
| --------- | -------- | --------- | ---------- |
| `/help`   | ✅        | ✅         | ✅          |
| `/start`  | ✅        | ✅         | ✅          |
| `/delete` | ❌        | ✅         | ✅          |
| `/open`   | ❌        | ❌         | ✅          |
| `/close`  | ❌        | ❌         | ✅          |
| `/ban`    | ❌        | ❌         | ✅          |
| `/unban`  | ❌        | ❌         | ✅          |

## 🤝 Contributing

Contributions are welcome. Here are some ways you can help:

1. **Report bugs**: If you find a bug, please report it by creating an
issue on GitHub;
2. **Request features**: Have an idea for a new feature?
Let us know by creating a feature request;
3. **Submit pull requests**: If you'd like to fix a bug or add a feature,
feel free to fork the repository and submit a pull request.
