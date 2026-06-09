import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";

const distDir = new URL("../dist/", import.meta.url);
const findings = [];

async function walk(directoryUrl) {
  const entries = await readdir(directoryUrl, { withFileTypes: true });

  for (const entry of entries) {
    const childUrl = new URL(`${entry.name}${entry.isDirectory() ? "/" : ""}`, directoryUrl);
    const childPath = join(childUrl.pathname);

    if (entry.isDirectory()) {
      await walk(childUrl);
      continue;
    }

    if (entry.name.endsWith(".map")) {
      findings.push(childPath);
      continue;
    }

    if (!/\.(html|css|js)$/i.test(entry.name)) {
      continue;
    }

    const contents = await readFile(childUrl, "utf8");
    if (contents.includes("sourceMappingURL")) {
      findings.push(`${childPath}: sourceMappingURL`);
    }
  }
}

await walk(distDir);

if (findings.length > 0) {
  console.error("Production build contains source map artifacts:");
  for (const finding of findings) {
    console.error(`- ${finding}`);
  }

  process.exit(1);
}

console.log("No production source maps found.");
