// Builds a large fictional Canvas course export (IMS Common Cartridge layout) in memory, for checking that TeachWell stays usable
// and fast with a full-size course. Sixteen weekly modules of about 31 items each (over 500 items in all), with the kinds of
// problems real courses have spread through them. Everything is invented; no real course is copied.

const crcTable = (() => {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c >>> 0;
  }
  return table;
})();
function crc32(data: Buffer) {
  let crc = 0xffffffff;
  for (const byte of data) crc = crcTable[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

/** A plain ZIP with every entry stored (no compression), which is all an .imscc needs to be. */
export function zip(files: Record<string, string>): Buffer {
  const locals: Buffer[] = [];
  const centrals: Buffer[] = [];
  let offset = 0;
  for (const [name, text] of Object.entries(files)) {
    const nameBytes = Buffer.from(name, "utf8");
    const data = Buffer.from(text, "utf8");
    const crc = crc32(data);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(0x0800, 6); // UTF-8 names
    local.writeUInt32LE(crc, 14);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(nameBytes.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt16LE(0x0800, 8);
    central.writeUInt32LE(crc, 16);
    central.writeUInt32LE(data.length, 20);
    central.writeUInt32LE(data.length, 24);
    central.writeUInt16LE(nameBytes.length, 28);
    central.writeUInt32LE(offset, 42);
    locals.push(local, nameBytes, data);
    centrals.push(central, nameBytes);
    offset += local.length + nameBytes.length + data.length;
  }
  const directory = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(Object.keys(files).length, 8);
  end.writeUInt16LE(Object.keys(files).length, 10);
  end.writeUInt32LE(directory.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, directory, end]);
}

const topics = [
  "Why data literacy matters", "Asking good questions", "Finding and judging sources", "Describing data", "Visualising distributions",
  "Comparing groups", "Relationships and correlation", "Sampling and bias", "Uncertainty and confidence", "Experiments and causation",
  "Cleaning messy data", "Telling stories with data", "Maps and geographic data", "Ethics and privacy", "Communicating to the public",
  "Final project studio",
];
const escape = (s: string) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
const paragraph = (week: number, n: number) =>
  `<p>In week ${week} we look closely at idea ${n}. Read the short case, note one question it raises, and connect it to your own field. ` +
  `The examples use invented data so that everyone can work with the same figures and compare their reasoning.</p>`;

export interface LargeCourse {
  bytes: Buffer;
  title: string;
  code: string;
  items: number;
  longTitle: string;
}

/** About 515 items: 17 modules, 48 text headers, 305 pages and 144 assignments. */
export function largeCourse(): LargeCourse {
  const title = "Data Literacy for Everyone (large sample)";
  const code = "DATA 900";
  const longTitle = "Week 3 reading 4: An extended case study of accessible data visualisation for multilingual learners in hybrid classrooms";
  const files: Record<string, string> = {};
  const resources: string[] = [];
  const modules: string[] = [];
  let items = 0;

  const page = (id: string, pageTitle: string, body: string) => {
    files[`wiki_content/${id}.html`] =
      `<html><head><title>${escape(pageTitle)}</title><meta name="workflow_state" content="active"/></head><body>${body}</body></html>`;
    resources.push(`<resource identifier="${id}" type="webcontent" href="wiki_content/${id}.html"><file href="wiki_content/${id}.html"/></resource>`);
  };
  const assignment = (id: string, asgTitle: string, body: string, dueAt: string | null, points: number | null) => {
    files[`${id}/${id}.html`] = `<html><head><title>${escape(asgTitle)}</title></head><body>${body}</body></html>`;
    files[`${id}/assignment_settings.xml`] =
      `<?xml version="1.0" encoding="UTF-8"?><assignment identifier="${id}" xmlns="http://canvas.instructure.com/xsd/cccv1p0">` +
      `<title>${escape(asgTitle)}</title><due_at>${dueAt ?? ""}</due_at>${points === null ? "" : `<points_possible>${points}</points_possible>`}` +
      `<workflow_state>published</workflow_state></assignment>`;
    resources.push(
      `<resource identifier="${id}" type="associatedcontent/imscc_xmlv1p1/learning-application-resource" href="${id}/${id}.html">` +
        `<file href="${id}/${id}.html"/><file href="${id}/assignment_settings.xml"/></resource>`,
    );
  };
  const item = (kind: string, itemTitle: string, ref: string | null, position: number) =>
    `<item identifier="item-${ref ?? `h${items}`}"><content_type>${kind}</content_type><workflow_state>active</workflow_state>` +
    `<title>${escape(itemTitle)}</title>${ref ? `<identifierref>${ref}</identifierref>` : ""}<position>${position}</position></item>`;

  // A start module, as a well-built course has.
  page("welcome", "Welcome and how this course works", "<h2>Welcome</h2><p>This fictional course is used to check TeachWell with a full-size course.</p>");
  modules.push(`<module identifier="mod-start"><title>Start here</title><workflow_state>active</workflow_state><position>1</position><items>${item("WikiPage", "Welcome and how this course works", "welcome", 1)}</items></module>`);
  items += 2;

  for (let week = 1; week <= 16; week++) {
    const entries: string[] = [];
    let position = 1;
    const add = (kind: string, itemTitle: string, ref: string | null) => {
      entries.push(item(kind, itemTitle, ref, position++));
      items++;
    };
    add("ContextModuleSubHeader", "Learn", null);
    page(`w${week}-overview`, `Week ${week} overview`, `<h2>This week</h2><p>Outcomes, activities and due dates for week ${week}.</p>`);
    add("WikiPage", `Week ${week} overview`, `w${week}-overview`);
    for (let r = 1; r <= 18; r++) {
      const id = `w${week}-r${r}`;
      const pageTitle = week === 3 && r === 4 ? longTitle : `Week ${week} reading ${r}: ${topics[week - 1]}, part ${r}`;
      const n = week * 100 + r;
      let body = `<h2>${escape(topics[week - 1])}</h2>${paragraph(week, r)}`;
      if (n % 5 === 0) body += `<img src="$IMS-CC-FILEBASE$/chart-${n}.png">`; // no text alternative
      if (n % 7 === 0) body += `<img src="$IMS-CC-FILEBASE$/IMG_${n}.jpg" alt="IMG_${n}.jpg">`; // alt text is a file name
      if (n % 9 === 0) body += `<p>For the dataset, <a href="https://example.edu/data/${n}">click here</a>.</p>`;
      if (n % 11 === 0) body += "<h3></h3>";
      if (r === 18) body += Array.from({ length: 45 }, (_, k) => paragraph(week, k + 1)).join(""); // one very long page a week
      page(id, pageTitle, body);
      add("WikiPage", pageTitle, id);
    }
    add("ContextModuleSubHeader", "Practise", null);
    for (let a = 1; a <= 8; a++) {
      const id = `w${week}-p${a}`;
      const day = String(1 + ((week * 7) % 27)).padStart(2, "0");
      // Every third practice task has no due date; every fourth has no points; the rest are all due on the same day.
      assignment(id, `Week ${week} practice ${a}`, `<p>Practise the week's idea ${a} with the sample data.</p>`,
        a % 3 === 0 ? null : `2027-0${1 + (week % 8)}-${day}T06:59:00`, a % 4 === 0 ? null : 5);
      add("Assignment", `Week ${week} practice ${a}`, id);
    }
    add("ContextModuleSubHeader", "Submit", null);
    assignment(`w${week}-project`, `Week ${week} project`, `<p>Apply this week's ideas to a dataset of your choice and explain what you found.</p>`,
      `2027-0${1 + (week % 8)}-${String(2 + ((week * 7) % 26)).padStart(2, "0")}T06:59:00`, 25);
    add("Assignment", `Week ${week} project`, `w${week}-project`);
    modules.push(
      `<module identifier="mod-w${week}"><title>Week ${week}: ${escape(topics[week - 1])}</title><workflow_state>active</workflow_state>` +
        `<position>${week + 1}</position><items>${entries.join("")}</items></module>`,
    );
    items++; // the module itself
  }

  files["imsmanifest.xml"] =
    `<?xml version="1.0" encoding="UTF-8"?><manifest identifier="g0fictional0large0course0001" xmlns="http://www.imsglobal.org/xsd/imsccv1p1/imscp_v1p1">` +
    `<metadata><lom xmlns="http://ltsc.ieee.org/xsd/imsccv1p1/LOM/resource"><general><title><string>Fictional export</string></title></general></lom></metadata>` +
    `<organizations/><resources>${resources.join("")}</resources></manifest>`;
  files["course_settings/course_settings.xml"] =
    `<?xml version="1.0" encoding="UTF-8"?><course identifier="g0fictional0large0course0001" xmlns="http://canvas.instructure.com/xsd/cccv1p0">` +
    `<title>${escape(title)}</title><course_code>${code}</course_code></course>`;
  files["course_settings/module_meta.xml"] =
    `<?xml version="1.0" encoding="UTF-8"?><modules xmlns="http://canvas.instructure.com/xsd/cccv1p0">${modules.join("")}</modules>`;
  return { bytes: zip(files), title, code, items, longTitle };
}
