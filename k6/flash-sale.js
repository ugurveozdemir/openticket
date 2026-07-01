import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const USER_POOL_SIZE = 40;
const SEAT_COUNT = 60;
const VUS = 150;

export const holdsWon = new Counter('flash_holds_won');
export const holdsConflict = new Counter('flash_holds_conflict');
export const ordersConfirmed = new Counter('flash_orders_confirmed');
export const ordersFailed = new Counter('flash_orders_unconfirmed');

export const options = {
  scenarios: {
    flash_sale: {
      executor: 'per-vu-iterations',
      vus: VUS,
      iterations: 1,
      maxDuration: '90s',
    },
  },
};

function jsonHeaders(token) {
  const headers = { 'Content-Type': 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  return { headers };
}

export function setup() {
  const tokens = [];
  for (let i = 0; i < USER_POOL_SIZE; i++) {
    const email = `flash_${Date.now()}_${i}@test.local`;
    const res = http.post(
      `${BASE_URL}/api/auth/register`,
      JSON.stringify({ email, password: 'FlashTest!123', displayName: `Flash User ${i}` }),
      jsonHeaders()
    );
    tokens.push(res.json('token'));
  }

  const venueRes = http.post(
    `${BASE_URL}/api/venues`,
    JSON.stringify({ name: 'k6 Flash Sale Venue', city: 'LoadTestCity' }),
    jsonHeaders()
  );
  const venueId = venueRes.json('id');

  http.post(
    `${BASE_URL}/api/venues/${venueId}/seats/bulk`,
    JSON.stringify({ rows: [{ section: 'A', rowLabel: '1', seatFrom: 1, seatTo: SEAT_COUNT }] }),
    jsonHeaders()
  );

  const eventRes = http.post(
    `${BASE_URL}/api/events`,
    JSON.stringify({
      venueId,
      title: 'k6 Flash Sale Event',
      startsAtUtc: new Date(Date.now() + 86400000).toISOString(),
    }),
    jsonHeaders()
  );
  const eventId = eventRes.json('id');

  http.post(`${BASE_URL}/api/events/${eventId}/publish`, JSON.stringify({ priceCents: 7500 }), jsonHeaders());

  const seatsRes = http.get(`${BASE_URL}/api/events/${eventId}/seats`, jsonHeaders(tokens[0]));
  const seatIds = seatsRes.json().map((s) => s.seatId);

  return { tokens, eventId, seatIds };
}

export default function (data) {
  const token = data.tokens[__VU % data.tokens.length];
  const seatId = data.seatIds[Math.floor(Math.random() * data.seatIds.length)];

  const holdRes = http.post(
    `${BASE_URL}/api/events/${data.eventId}/seats/hold`,
    JSON.stringify({ seatIds: [seatId] }),
    jsonHeaders(token)
  );

  check(holdRes, { 'hold: 201 or 409': (r) => r.status === 201 || r.status === 409 });

  if (holdRes.status !== 201) {
    holdsConflict.add(1);
    return;
  }
  holdsWon.add(1);

  const orderId = holdRes.json('orderId');

  const payRes = http.post(`${BASE_URL}/api/orders/${orderId}/pay`, null, jsonHeaders(token));
  check(payRes, { 'pay: 202': (r) => r.status === 202 });

  let status = 'PendingPayment';
  for (let i = 0; i < 15 && status === 'PendingPayment'; i++) {
    sleep(1);
    const orderRes = http.get(`${BASE_URL}/api/orders/${orderId}`, jsonHeaders(token));
    status = orderRes.json('status');
  }

  if (status === 'Confirmed') {
    ordersConfirmed.add(1);
  } else {
    ordersFailed.add(1);
  }
}
