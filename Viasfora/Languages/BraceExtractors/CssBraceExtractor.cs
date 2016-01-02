﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Winterdom.Viasfora.Rainbow;
using Winterdom.Viasfora.Util;

namespace Winterdom.Viasfora.Languages.BraceExtractors {
  public class CssBraceExtractor : IBraceExtractor {
    const int stText = 0;
    const int stComment = 1;
    const int stSingleQuotedString = 2;
    const int stDoubleQuotedString = 3;
    private int state;

    public String BraceList {
      get { return "(){}[]"; }
    }

    public CssBraceExtractor() {
    }

    public void Reset(int state) {
      this.state = stText;
    }

    public bool Extract(ITextChars tc, ref CharPos pos) {
      while ( !tc.EndOfLine ) {
        switch ( this.state ) {
          case stComment: ParseComment(tc); break;
          case stSingleQuotedString: ParseString(tc); break;
          case stDoubleQuotedString: ParseDString(tc); break;
          default:
            return ParseText(tc, ref pos);
        }
      }
      return false;
    }

    private bool ParseText(ITextChars tc, ref CharPos pos) {
      pos = CharPos.Empty;
      while ( !tc.EndOfLine ) {
        if ( tc.Char() == '/' && tc.NChar() == '*' ) {
          this.state = stComment;
          tc.Skip(2);
          ParseComment(tc);
        } else if ( tc.Char() == '/' && tc.NChar() == '/' ) {
          // CSS doesn't really support single-line comments, 
          // but SASS does, and it doesn't harm too
          // much to implement it as a single thing
          tc.SkipRemainder();
        } else if ( tc.Char() == '"' ) {
          this.state = stDoubleQuotedString;
          tc.Next();
          ParseDString(tc);
        } else if ( tc.Char() == '\'' ) {
          this.state = stSingleQuotedString;
          tc.Next();
          ParseString(tc);
        } else if ( this.BraceList.Contains(tc.Char()) ) {
          pos = new CharPos(tc.Char(), tc.AbsolutePosition);
          tc.Next();
          return true;
        } else {
          tc.Next();
        }
      }
      return false;
    }
    private void ParseString(ITextChars tc) {
      ParseString(tc, '\'');
    }
    private void ParseDString(ITextChars tc) {
      ParseString(tc, '"');
    }
    private void ParseString(ITextChars tc, char quote) {
      while ( !tc.EndOfLine ) {
        if ( tc.Char() == '\\' ) {
          // escape sequence
          // could be 1-6 hex digits or something else
          tc.Skip(2);
        } else if ( tc.Char() == quote ) {
          tc.Next();
          this.state = stText;
          break;
        }
        tc.Next();
      }
    }
    private void ParseComment(ITextChars tc) {
      while ( !tc.EndOfLine ) {
        if ( tc.Char() == '*' && tc.NChar() == '/' ) {
          tc.Skip(2);
          this.state = stText;
          return;
        }
        tc.Next();
      }
    }

  }
}
