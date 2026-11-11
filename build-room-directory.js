'use strict';

const fs = require('fs');
const path = require('path');
const { entries } = require('./room_catalog.json');
const destination = path.resolve(process.argv[2]);
const campuses = new Map([['north', new Map()], ['south', new Map()]]);
const ids = new Set();

function group(groups, id, name) {
  if (!groups.has(id)) groups.set(id, { id, name, children: new Map(), rooms: [] });
  const value = groups.get(id);
  if (value.name !== name) throw new Error('Inconsistent catalog name: ' + name);
  return value;
}

for (const room of entries) {
  const [campus, building, floor] = room.id.split('|');
  if (campus !== room.campus || !campuses.has(campus) || !/^\d+$/.test(building) ||
      !/^\d+$/.test(floor) || ids.has(room.id)) {
    throw new Error('Invalid catalog entry: ' + room.id);
  }
  ids.add(room.id);
  const buildingGroup = group(campuses.get(campus), building, room.building);
  group(buildingGroup.children, floor, room.floor).rooms.push(room);
}

function write(relativePath, value) {
  const file = path.join(destination, relativePath);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify(value), 'utf8');
}

const collator = new Intl.Collator('zh-CN', { numeric: true });
function choices(groups) {
  return [...groups.values()].map(({ id, name }) => ({ id, name }))
    .sort((a, b) => collator.compare(a.name, b.name));
}

for (const [campus, buildings] of campuses) {
  write(path.join(campus, 'buildings.json'), choices(buildings));
  for (const [building, { children: floors }] of buildings) {
    write(path.join(campus, building, 'floors.json'), choices(floors));
    for (const [floor, { rooms }] of floors) {
      rooms.sort((a, b) => collator.compare(a.name, b.name));
      write(path.join(campus, building, floor + '.json'), rooms);
    }
  }
}
