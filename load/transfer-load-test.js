import http from "k6/http";
import { check } from "k6";
import { Counter } from "k6/metrics";

// Basic load smoke test (stretch goal) — not a capacity/SLO study. It exists to
// answer one question: under a moderate burst of concurrent traffic, does the API
// stay correct (no 5xx, no crashes) and reasonably responsive? See load/README.md
// for the run instructions and results.

const BASE_URL = __ENV.BASE_URL || "http://127.0.0.1:5299";
const BEARER_TOKEN = __ENV.BEARER_TOKEN || "novawallet-dev-token";
const WALLET_POOL_SIZE = 20;
const FUNDING_KOBO = 10_000_000; // NGN 100,000 per pooled wallet — well under the daily limit per wallet

const authHeaders = {
  headers: {
    Authorization: `Bearer ${BEARER_TOKEN}`,
    "Content-Type": "application/json",
  },
};

export const options = {
  scenarios: {
    smoke: {
      executor: "ramping-vus",
      startVUs: 0,
      stages: [
        { duration: "10s", target: 20 },
        { duration: "20s", target: 20 },
        { duration: "5s", target: 0 },
      ],
    },
  },
  thresholds: {
    // The one hard requirement: the API must never 5xx under this load.
    http_req_failed: ["rate==0"],
    "http_req_duration{endpoint:transfer}": ["p(95)<500"],
  },
};

const serverErrors = new Counter("server_errors");

export function setup() {
  const walletIds = [];
  for (let i = 0; i < WALLET_POOL_SIZE; i++) {
    const createRes = http.post(`${BASE_URL}/wallets`, null, authHeaders);
    if (createRes.status !== 201) {
      throw new Error(`Setup failed to create wallet: ${createRes.status} ${createRes.body}`);
    }
    const wallet = JSON.parse(createRes.body);

    const creditRes = http.post(
      `${BASE_URL}/wallets/${wallet.id}/credit`,
      JSON.stringify({ amountKobo: FUNDING_KOBO }),
      authHeaders
    );
    if (creditRes.status !== 200) {
      throw new Error(`Setup failed to fund wallet: ${creditRes.status} ${creditRes.body}`);
    }

    walletIds.push(wallet.id);
  }
  return { walletIds };
}

export default function (data) {
  const { walletIds } = data;
  const fromIndex = Math.floor(Math.random() * walletIds.length);
  let toIndex = Math.floor(Math.random() * walletIds.length);
  if (toIndex === fromIndex) {
    toIndex = (toIndex + 1) % walletIds.length;
  }

  const payload = JSON.stringify({
    fromWalletId: walletIds[fromIndex],
    toWalletId: walletIds[toIndex],
    amountKobo: 100,
  });

  const res = http.post(`${BASE_URL}/transfers`, payload, {
    headers: {
      ...authHeaders.headers,
      "Idempotency-Key": `${__VU}-${__ITER}-${Date.now()}`,
    },
    tags: { endpoint: "transfer" },
  });

  const ok = check(res, {
    // 201 (moved funds) and 422 (that source wallet's daily limit reached under
    // sustained load) are both legitimate outcomes here; anything 5xx is not.
    "status is 201 or 422": (r) => r.status === 201 || r.status === 422,
    "status is not 5xx": (r) => r.status < 500,
  });

  if (!ok) {
    serverErrors.add(1);
    console.error(`Unexpected response: ${res.status} ${res.body}`);
  }
}
