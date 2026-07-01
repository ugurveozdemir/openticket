import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const VUS = 200;

export const holdsWon = new Counter('holds_won');

export const options = {
  scenarios: {
    hold_race: {
      executor: 'per-vu-iterations',
      vus: VUS,
      iterations: 1,
      maxDuration: '30s',
    },
  },
  thresholds: {
    holds_won: ['count==1'],
  },
};

function jsonHeaders(token) {
  const headers = { 'Content-Type': 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  return { headers };
}

export function setup() {
  const email = `race_${Date.now()}@test.local`;
  const registerRes = http.post(
    `${BASE_URL}/api/auth/register`,
    JSON.stringify({ email, password: 'RaceTest!123', displayName: 'Race Tester' }),
    jsonHeaders()
  );
  const token = registerRes.json('token');

  const venueRes = http.post(
    `${BASE_URL}/api/venues`,
    JSON.stringify({ name: 'k6 Hold Race Venue', city: 'LoadTestCity' }),
    jsonHeaders()
  );
  const venueId = venueRes.json('id');

  http.post(
    `${BASE_URL}/api/venues/${venueId}/seats/bulk`,
    JSON.stringify({ rows: [{ section: 'A', rowLabel: '1', seatFrom: 1, seatTo: 1 }] }),
    jsonHeaders()
  );

  const eventRes = http.post(
    `${BASE_URL}/api/events`,
    JSON.stringify({
      venueId,
      title: 'k6 Hold Race Event',
      startsAtUtc: new Date(Date.now() + 86400000).toISOString(),
    }),
    jsonHeaders()
  );
  const eventId = eventRes.json('id');

  http.post(`${BASE_URL}/api/events/${eventId}/publish`, JSON.stringify({ priceCents: 5000 }), jsonHeaders());

  const seatsRes = http.get(`${BASE_URL}/api/events/${eventId}/seats`, jsonHeaders(token));
  const seatId = seatsRes.json()[0].seatId;

  return { token, eventId, seatId };
}

export default function (data) {
  const res = http.post(
    `${BASE_URL}/api/events/${data.eventId}/seats/hold`,
    JSON.stringify({ seatIds: [data.seatId] }),
    jsonHeaders(data.token)
  );

  check(res, { 'status is 201 (won) or 409 (lost)': (r) => r.status === 201 || r.status === 409 });

  if (res.status === 201) {
    holdsWon.add(1);
  }
}
