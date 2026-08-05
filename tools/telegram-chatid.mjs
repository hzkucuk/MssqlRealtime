// Finds the chat id(s) your Telegram bot can send to.
//
//   1. Create the bot with @BotFather (/newbot) and copy its token
//   2. Send any message to the bot (or add it to a group and write there)
//   3. node tools/telegram-chatid.mjs <TOKEN>
//
// Paste the printed id into the app: ⚙️ → Telegram → "Sohbet (chat) kimliği".

const token = process.argv[2];

if (!token) {
	console.error('Kullanim: node tools/telegram-chatid.mjs <BOT_TOKEN>');
	process.exit(1);
}

const response = await fetch(`https://api.telegram.org/bot${token}/getUpdates`);
const data = await response.json();

if (!data.ok) {
	console.error(`Telegram hatasi: ${data.description ?? 'bilinmeyen'}`);
	console.error('Token yanlis olabilir. @BotFather -> /mybots -> API Token ile karsilastirin.');
	process.exit(1);
}

if (data.result.length === 0) {
	console.log('Hic mesaj gorunmuyor.');
	console.log('Bota Telegram uzerinden bir mesaj gonderip (veya /start basip) tekrar calistirin.');
	console.log('Grup icin: botu gruba ekleyin ve gruba bir mesaj yazin.');
	process.exit(0);
}

const seen = new Map();

for (const update of data.result) {
	const chat = update.message?.chat ?? update.channel_post?.chat ?? update.my_chat_member?.chat;
	if (chat) seen.set(chat.id, chat);
}

console.log('\nBulunan sohbetler:\n');

for (const [id, chat] of seen) {
	const name = chat.title ?? [chat.first_name, chat.last_name].filter(Boolean).join(' ') ?? chat.username;
	const kind = chat.type === 'private' ? 'ozel' : chat.type;
	console.log(`  chat id: ${id}\n     tur : ${kind}\n     ad  : ${name}\n`);
}

console.log('Bu id\'yi uygulamada Telegram ayarlarina yapistirin.');
console.log('Not: grup id\'leri eksi ile baslar (-100...), bu normaldir.\n');
