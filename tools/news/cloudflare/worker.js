// Будильник для сборщика новостей: GitHub прореживает cron в Actions до раза в несколько часов,
// поэтому раз в 30 минут Cloudflare сам запускает workflow news.yml через workflow_dispatch.
// Секрет GH_TOKEN — fine-grained токен только на Invizet/echo-angmara, право Actions: Read and write.
const DISPATCH = "https://api.github.com/repos/Invizet/echo-angmara/actions/workflows/news.yml/dispatches";

export default {
  async scheduled(event, env, ctx) {
    const r = await fetch(DISPATCH, {
      method: "POST",
      headers: {
        "Authorization": `Bearer ${env.GH_TOKEN}`,
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "echo-angmara-news-cron",
      },
      body: JSON.stringify({ ref: "main" }),
    });
    if (r.status !== 204) {
      throw new Error(`dispatch ${r.status}: ${await r.text()}`);  // видно в логах воркера
    }
  },
};
