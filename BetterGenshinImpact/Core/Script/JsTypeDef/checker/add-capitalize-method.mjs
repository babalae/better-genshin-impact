#!/usr/bin/env node
/**
 * add-capitalize-method-recast.js
 * -------------------------------------------------
 * - 为 class 与 declare const 对象中首字母小写成员生成首字母大写 alias
 * - 支持：实例/静态 方法与属性（复制 readonly；修饰符顺序为 static readonly）
 * - 支持：declare const X: { ... }，包含 TSPropertySignature 与 TSMethodSignature
 * - 忽略 constructor
 * - 保留注释与空行；插入区块上下加注释
 * - 文件头添加 AUTO-GENERATED 提示
 */

import fs from "fs";
import path from "path";
import recast from "recast";
import * as babelParser from "@babel/parser";

const isLower = (s) => /^[a-z]/.test(s);
const cap1 = (s) => (s ? s[0].toUpperCase() + s.slice(1) : s);

function typeofRef_TSTypeAnnotation(containerName, memberName, { staticRef = false, constObj = false } = {}) {
  const left = constObj || staticRef
    ? { type: "Identifier", name: containerName }
    : {
        type: "TSQualifiedName",
        left: { type: "Identifier", name: containerName },
        right: { type: "Identifier", name: "prototype" },
      };

  return {
    type: "TSTypeAnnotation",
    typeAnnotation: {
      type: "TSTypeQuery",
      exprName: {
        type: "TSQualifiedName",
        left,
        right: { type: "Identifier", name: memberName },
      },
    },
  };
}

/** ---------- Class 处理 ---------- */

function getClassMemberInfo(m) {
  const ok =
    m &&
    (m.type === "TSDeclareMethod" ||
      m.type === "ClassMethod" ||
      m.type === "ClassProperty" ||
      m.type === "TSPropertySignature");
  if (!ok) return null;
  if (!m.key || m.key.type !== "Identifier") return null;
  const name = m.key.name;
  if (name === "constructor") return null; // ✅ 忽略 constructor
  const isStatic = !!m.static;
  const isReadonly = !!m.readonly;
  return { name, isStatic, isReadonly };
}


/**
 * 为类成员添加首字母大写的别名
 * @param {recast.types.namedTypes.ClassDeclaration} classDeclNode 
 * @param {recast.types.namedTypes.ClassBody} classBodyNode 
 */
function addAliasesToClassBody(classDeclNode, classBodyNode) {
  const body = classBodyNode.body;
  const className = classDeclNode.id?.name || "Anonymous";
  const existing = new Set(
    body.map((m) => (m?.key?.type === "Identifier" ? m.key.name : null)).filter(Boolean)
  );
  const doneInst = new Set();
  const doneStat = new Set();
  const toAdd = [];

  for (const m of body) {
    const info = getClassMemberInfo(m);
    if (!info) continue;
    const { name, isStatic, isReadonly } = info;
    if (!isLower(name)) continue;
    const done = isStatic ? doneStat : doneInst;
    if (done.has(name)) continue;
    done.add(name);
    const alias = cap1(name);
    if (existing.has(alias)) continue;

    /**
     * @type {recast.types.namedTypes.ClassProperty}
     */
    const aliasNode = {
      type: "ClassProperty",
      key: { type: "Identifier", name: alias },
      static: isStatic,
      readonly: isReadonly,
      declare: true,
      value: null,
      typeAnnotation: typeofRef_TSTypeAnnotation(className, name, { staticRef: isStatic }),
    };
    toAdd.push(aliasNode);
    existing.add(alias);
  }

  if (toAdd.length) {
    const beginBlock = recast.types.builders.commentLine(" ==== BEGIN AUTO-GENERATED ALIASES ====", true, false);
    const endBlock   = recast.types.builders.commentLine(" ==== END AUTO-GENERATED ALIASES ====", false, true);
    toAdd[0].comments = (toAdd[0].comments || []).concat(beginBlock);
    toAdd.push({ type: "EmptyStatement", comments: [endBlock] });

    body.push(...toAdd);
  }
}

/** ---------- declare const 处理 ---------- */
function addAliasesToDeclareConst(varDeclNode) {
  if (!varDeclNode.declarations || !varDeclNode.declarations.length) return;
  for (const d of varDeclNode.declarations) {
    if (!d.id || d.id.type !== "Identifier") continue;
    const constName = d.id.name;
    const t = d.id.typeAnnotation?.typeAnnotation;
    if (!t || t.type !== "TSTypeLiteral") continue;

    const members = t.members;
    if (!Array.isArray(members)) continue;

    const existing = new Set(
      members.map((m) => (m?.key?.type === "Identifier" ? m.key.name : null)).filter(Boolean)
    );

    const toAdd = [];
    for (const m of members) {
      if (!m.key || m.key.type !== "Identifier") continue;
      const name = m.key.name;
      if (name === "constructor") continue; // ✅ 忽略 constructor
      if (!isLower(name)) continue;

      const alias = cap1(name);
      if (existing.has(alias)) continue;

      const aliasNode = {
        type: "TSPropertySignature",
        key: { type: "Identifier", name: alias },
        typeAnnotation: typeofRef_TSTypeAnnotation(constName, name, { constObj: true }),
        readonly: !!m.readonly,
      };
      toAdd.push(aliasNode);
      existing.add(alias);
    }

    if (toAdd.length) {
      const beginBlock = recast.types.builders.commentLine(" ==== BEGIN AUTO-GENERATED ALIASES ====", true, false);
      const endBlock   = recast.types.builders.commentLine(" ==== END AUTO-GENERATED ALIASES ====", false, true);
      toAdd[0].comments = (toAdd[0].comments || []).concat(beginBlock);
      toAdd.push({ type: "EmptyStatement", comments: [endBlock] });
        
      members.push(...toAdd);
    }
  }
}

/** ---------- 遍历 ---------- */
function transform(code) {
  const ast = recast.parse(code, {
    parser: {
      parse: (src) =>
        babelParser.parse(src, {
          sourceType: "module",
          plugins: [
            "typescript",
            "classProperties",
            "decorators-legacy",
            "classPrivateProperties",
            "classPrivateMethods",
          ],
        }),
    },
  });

  recast.types.visit(ast, {
    visitClassDeclaration(p) {
      addAliasesToClassBody(p.node, p.node.body);
      return false;
    },
    visitVariableDeclaration(p) {
      addAliasesToDeclareConst(p.node);
      return false;
    },
  });

  let out = recast.print(ast, { quote: "double", tabWidth: 2, trailingComma: false }).code;
  if (!out.startsWith("// AUTO-GENERATED FILE")) {
    out =
      "// AUTO-GENERATED FILE. DO NOT EDIT.\n" +
      "// This file was generated by add-capitalize-method-recast.js\n\n" +
      out;
  }
  return out;
}

/** CLI */
function main() {
  const file = process.argv[2];
  const outfile = process.argv[3] || (file ? file.replace(/\.d?\.ts$/i, ".alias.d.ts") : "");
  if (!file) {
    console.error("Usage: node add-capitalize-method-recast.js <file.d.ts> [output]");
    process.exit(1);
  }
  const abs = path.resolve(file);
  if (!fs.existsSync(abs)) {
    console.error("File not found:", abs);
    process.exit(1);
  }

  const text = fs.readFileSync(abs, "utf8");
  const out = transform(text);
  fs.writeFileSync(outfile, out, "utf8");
  console.log("✅ Done. Output written to:", outfile);
}

main();
