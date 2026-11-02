// Kiểm thử firestore.rules trên Firestore Emulator.
// Chạy: cd firebase/rules-tests && npm install && npm test
import { readFileSync } from 'node:fs';
import { after, before, beforeEach, describe, it } from 'node:test';
import { assertFails, assertSucceeds, initializeTestEnvironment } from '@firebase/rules-unit-testing';
import {
  Timestamp, arrayUnion, collection, deleteDoc, doc, getDoc, getDocs, setDoc, updateDoc, writeBatch,
} from 'firebase/firestore';

let env;
const DAY = 24 * 3600 * 1000;
const future = () => Timestamp.fromMillis(Date.now() + 30 * DAY);
const past = () => Timestamp.fromMillis(Date.now() - DAY);

function profile(overrides = {}) {
  return {
    email: 'a@shop.kr', storeName: '행복폰', phone: '01012345678',
    status: 'active', expiresAt: future(), maxDevices: 2, deviceIds: [],
    createdAt: Timestamp.now(), updatedAt: Timestamp.now(),
    ...overrides,
  };
}

const device = () => ({
  machineName: 'PC-1', appVersion: '1.0.0', firstSeenAt: Timestamp.now(), lastSeenAt: Timestamp.now(),
});

async function seed(path, data) {
  await env.withSecurityRulesDisabled(ctx => setDoc(doc(ctx.firestore(), path), data));
}

const as = uid => env.authenticatedContext(uid).firestore();

function registerBatch(uid, ...machineIds) {
  const db = as(uid);
  const b = writeBatch(db);
  for (const id of machineIds) b.set(doc(db, `users/${uid}/devices/${id}`), device());
  b.update(doc(db, `users/${uid}`), { deviceIds: arrayUnion(machineIds[0]) });
  return b.commit();
}

before(async () => {
  env = await initializeTestEnvironment({
    projectId: 'demo-solapisender',
    firestore: { rules: readFileSync(new URL('../firestore.rules', import.meta.url), 'utf8') },
  });
});

after(() => env.cleanup());

beforeEach(async () => {
  await env.clearFirestore();
  await seed('users/alice', profile());
  await seed('users/bob', profile({ email: 'b@shop.kr' }));
  await seed('users/locked', profile({ status: 'locked' }));
  await seed('users/expired', profile({ expiresAt: past() }));
  await seed('admins/admin', { passwordHash: 'x' });
});

describe('users/{uid}', () => {
  it('chưa đăng nhập thì không đọc được', async () => {
    await assertFails(getDoc(doc(env.unauthenticatedContext().firestore(), 'users/alice')));
  });

  it('chỉ đọc được hồ sơ của chính mình', async () => {
    await assertSucceeds(getDoc(doc(as('alice'), 'users/alice')));
    await assertFails(getDoc(doc(as('alice'), 'users/bob')));
    await assertFails(getDocs(collection(as('alice'), 'users')));
  });

  it('tài khoản bị khóa/hết hạn vẫn đọc được hồ sơ (để app hiển thị lý do)', async () => {
    await assertSucceeds(getDoc(doc(as('locked'), 'users/locked')));
    await assertSucceeds(getDoc(doc(as('expired'), 'users/expired')));
  });

  it('không tự sửa trạng thái, hạn dùng, số máy', async () => {
    const db = as('alice');
    await assertFails(updateDoc(doc(db, 'users/alice'), { status: 'active', expiresAt: future() }));
    await assertFails(updateDoc(doc(db, 'users/alice'), { maxDevices: 99 }));
    await assertFails(setDoc(doc(db, 'users/alice'), profile({ maxDevices: 99 })));
    await assertFails(deleteDoc(doc(db, 'users/alice')));
    await assertFails(setDoc(doc(db, 'users/mallory'), profile()));
  });

  it('không thêm deviceIds khi không tạo device tương ứng', async () => {
    await assertFails(updateDoc(doc(as('alice'), 'users/alice'), { deviceIds: arrayUnion('pc1') }));
  });
});

describe('đăng ký thiết bị', () => {
  it('tạo device + thêm deviceIds trong cùng batch → OK', async () => {
    await assertSucceeds(registerBatch('alice', 'pc1'));
  });

  it('tạo device mà không thêm deviceIds → bị chặn', async () => {
    await assertFails(setDoc(doc(as('alice'), 'users/alice/devices/pc1'), device()));
  });

  it('2 thiết bị trong 1 batch nhưng chỉ thêm 1 id → bị chặn', async () => {
    await assertFails(registerBatch('alice', 'pc1', 'pc2'));
  });

  it('id thêm vào khác device được tạo → bị chặn', async () => {
    const db = as('alice');
    const b = writeBatch(db);
    b.set(doc(db, 'users/alice/devices/pc1'), device());
    b.update(doc(db, 'users/alice'), { deviceIds: arrayUnion('pc-other') });
    await assertFails(b.commit());
  });

  it('vượt maxDevices → bị chặn', async () => {
    await seed('users/alice', profile({ maxDevices: 2, deviceIds: ['pc1', 'pc2'] }));
    await assertFails(registerBatch('alice', 'pc3'));
  });

  it('đăng ký đến đúng giới hạn → OK', async () => {
    await seed('users/alice', profile({ maxDevices: 2, deviceIds: ['pc1'] }));
    await assertSucceeds(registerBatch('alice', 'pc2'));
  });

  it('tài khoản bị khóa / hết hạn không đăng ký được', async () => {
    await assertFails(registerBatch('locked', 'pc1'));
    await assertFails(registerBatch('expired', 'pc1'));
  });

  it('không đăng ký thiết bị cho người khác', async () => {
    const db = as('alice');
    const b = writeBatch(db);
    b.set(doc(db, 'users/bob/devices/pcX'), device());
    b.update(doc(db, 'users/bob'), { deviceIds: arrayUnion('pcX') });
    await assertFails(b.commit());
  });

  it('cập nhật lastSeenAt/appVersion OK; sửa firstSeenAt hoặc xóa → bị chặn', async () => {
    await seed('users/alice', profile({ deviceIds: ['pc1'] }));
    await seed('users/alice/devices/pc1', device());
    const ref = doc(as('alice'), 'users/alice/devices/pc1');
    await assertSucceeds(updateDoc(ref, { lastSeenAt: Timestamp.now(), appVersion: '1.0.1', machineName: 'PC-1' }));
    await assertFails(updateDoc(ref, { firstSeenAt: Timestamp.now() }));
    await assertFails(deleteDoc(ref));
  });
});

describe('templates', () => {
  const tpl = () => ({ name: '신모델', content: '#{이름}님 안녕하세요', subject: null, isAd: false,
    createdAt: Timestamp.now(), updatedAt: Timestamp.now() });

  it('chủ tài khoản đang hoạt động: tạo/đọc/sửa/xóa', async () => {
    const db = as('alice');
    const ref = doc(db, 'users/alice/templates/t1');
    await assertSucceeds(setDoc(ref, tpl()));
    await assertSucceeds(getDocs(collection(db, 'users/alice/templates')));
    await assertSucceeds(setDoc(ref, { ...tpl(), name: '수정' }));
    await assertSucceeds(deleteDoc(ref));
  });

  it('người khác không truy cập được', async () => {
    await seed('users/alice/templates/t1', tpl());
    await assertFails(getDoc(doc(as('bob'), 'users/alice/templates/t1')));
    await assertFails(setDoc(doc(as('bob'), 'users/alice/templates/t2'), tpl()));
  });

  it('tài khoản bị khóa / hết hạn bị chặn', async () => {
    await assertFails(setDoc(doc(as('locked'), 'users/locked/templates/t1'), tpl()));
    await assertFails(getDocs(collection(as('expired'), 'users/expired/templates')));
  });

  it('dữ liệu sai định dạng bị chặn', async () => {
    const ref = doc(as('alice'), 'users/alice/templates/t1');
    await assertFails(setDoc(ref, { ...tpl(), hacked: true }));
    await assertFails(setDoc(ref, { ...tpl(), name: '' }));
    await assertFails(setDoc(ref, { ...tpl(), content: 'x'.repeat(4001) }));
  });
});

describe('settings', () => {
  const s = () => ({ defaultSender: '0215881234', lowBalanceThreshold: 10000, optOutNumber: null,
    priceSms: 18, priceLms: 45, priceMms: 110, updatedAt: Timestamp.now() });

  it('chỉ document "app"', async () => {
    const db = as('alice');
    await assertSucceeds(setDoc(doc(db, 'users/alice/settings/app'), s()));
    await assertSucceeds(getDoc(doc(db, 'users/alice/settings/app')));
    await assertFails(setDoc(doc(db, 'users/alice/settings/other'), s()));
    await assertFails(setDoc(doc(db, 'users/alice/settings/app'), { ...s(), role: 'admin' }));
  });
});

describe('logs', () => {
  const log = () => ({ type: 'send', message: '6/6', meta: { groupIds: ['G1'] }, machineId: 'pc1', createdAt: Timestamp.now() });

  it('chỉ ghi thêm, không đọc/sửa/xóa', async () => {
    const db = as('alice');
    await assertSucceeds(setDoc(doc(db, 'users/alice/logs/l1'), log()));
    await seed('users/alice/logs/l2', log());
    await assertFails(getDoc(doc(db, 'users/alice/logs/l2')));
    await assertFails(updateDoc(doc(db, 'users/alice/logs/l2'), { message: 'x' }));
    await assertFails(deleteDoc(doc(db, 'users/alice/logs/l2')));
  });

  it('tài khoản bị khóa vẫn ghi log được; không ghi vào log người khác', async () => {
    await assertSucceeds(setDoc(doc(as('locked'), 'users/locked/logs/l1'), log()));
    await assertFails(setDoc(doc(as('alice'), 'users/bob/logs/l1'), log()));
  });
});

describe('admins và đường dẫn khác', () => {
  it('client không đọc/ghi được admins', async () => {
    await assertFails(getDoc(doc(as('alice'), 'admins/admin')));
    await assertFails(setDoc(doc(as('alice'), 'admins/x'), { passwordHash: 'y' }));
  });

  it('collection lạ bị chặn', async () => {
    await assertFails(setDoc(doc(as('alice'), 'misc/x'), { a: 1 }));
  });
});
