import 'babel-polyfill'
import { createStore, combineReducers, applyMiddleware, compose } from 'redux'
import thunk from 'redux-thunk'
import React, { PropTypes } from 'react'
import { connect, Provider  } from 'react-redux'
import { render } from 'react-dom'

let websocket;
let wsUri = "ws://localhost:8083/websocket";
let output;

const event = (state, action) => {
  switch (action.type) {
    case 'StartedEvent':
      return {
        id: action.id,
        text: action.text,
        type: action.type
      }
    case 'FinishedEvent':
      if (state.id != action.id) {
          return state
      }
      return Object.assign({}, state, {
          type: action.type,
          text: action.text
      })
    case 'FailedEvent':
      if (state.id != action.id) {
          return state
      }
      return Object.assign({}, state, {
          type: action.type,
          text: action.text
      })
    default:
      return state
  }
}

const events = (state = [], action) => {
  switch (action.type) {
    case 'StartedEvent':
      return [
        ...state,
        event(undefined, action)
      ]
    case 'FinishedEvent':
      return state.map(e =>
        event(e, action)
      )
      return
    case 'FailedEvent':
        return state.map(e =>
          event(e, action)
        )
        return
    default:
      return state
  }
}

const eventsApp = combineReducers({
  events
})

function startedEvent(_id, text) {
  return {
    type: 'StartedEvent',
    id: _id,
    text
  }
}

function finishedEvent(_id, text) {
  return {
    type: 'FinishedEvent',
    id: _id,
    text
  }
}

function failedEvent(_id, text) {
  return {
    type: 'FailedEvent',
    id: _id,
    text
  }
}

const StreamingEvent = ({ type, text }) => (
  <li className={"flex-item " + type} title={text}>
  </li>
)

StreamingEvent.propTypes = {
  type: PropTypes.string.isRequired,
  //text: PropTypes.string.isRequired
}

const StreamingEventList = ({ events }) => (
  <ul className="flex-container">
    {events.map(event =>
      <StreamingEvent
        key={event.id}
        {...event}
      />
    )}
  </ul>
)

StreamingEventList.propTypes = {
    events: PropTypes.arrayOf(PropTypes.shape({
    id: PropTypes.number.isRequired,
    //text: PropTypes.string.isRequired
  }).isRequired).isRequired,
}

const Header = ({ average, max }) => (
  <ul>
    <li>Average: {average}</li>
    <li>Maximum: {max}</li>
  </ul>
)

Header.propTypes = {
  average: PropTypes.number.isRequired,
  max: PropTypes.number.isRequired
}

const mapStateToProps = (state) => {
  return {
    events: state.events
  }
}

const mapStateToHeaderProps = (state) => {
  let f = state.events.filter(e => e.type == "FinishedEvent")
  let finished = f.map(e => parseFloat(e.text))
  return {
    average: finished.reduce((a,b)=>a + b, 0) / finished.length,
    max: Math.max.apply(Math, finished)
  }
}

const VisibleHeader = connect(
  mapStateToHeaderProps
)(Header)

const VisibleEventList = connect(
  mapStateToProps
)(StreamingEventList)

const App = () => (
  <div>
    <VisibleHeader />
    <VisibleEventList />
  </div>
)

var initializing = true;
var store;
function init()
{
  output = document.getElementById("output");
  testWebSocket();
}
function testWebSocket()
{
  websocket = new WebSocket(wsUri);
  websocket.onopen = function(evt) { onOpen(evt) };
  websocket.onclose = function(evt) { onClose(evt) };
  websocket.onmessage = function(evt) { onMessage(evt) };
  websocket.onerror = function(evt) { onError(evt) };
}
function onOpen(evt)
{
  writeToScreen("CONNECTED");
  doSend("INIT");
}
function onClose(evt)
{
  writeToScreen("DISCONNECTED");
}

function onMessage(evt)
{
  function getNumericId(item)
  {
    let s = item.split("-")
    return s.pop()
  }

  function dispatchEvent(event) {
    switch (event.Case) {
      case 'StartedEvent':
        //console.log("Started")
        store.dispatch(startedEvent(parseInt(getNumericId(event.Item)), ""))
        break;
      case 'FinishedEvent':
        //console.log("Finished")
        store.dispatch(finishedEvent(parseInt(getNumericId(event.Item1)), parseFloat(event.Item2)))
        break;
      case 'FailedEvent':
          store.dispatch(failedEvent(parseInt(getNumericId(event.Item1)), parseFloat(event.Item2)))
          break;
      default:
        console.log("Unknown event")
        break;
    }
  }

  if (initializing) {
    initializing = false
    let data = JSON.parse(evt.data)
    let reversed = data.value.contents.reverse();
    var initialState;

    const finalCreateStore = compose(
      applyMiddleware(thunk),
      window.devToolsExtension ? window.devToolsExtension() : f => f
    )(createStore);

    store = finalCreateStore(eventsApp)//, evt.data.value.contents)

    for (let e of reversed) {
        dispatchEvent(e[0]);
    }

    render(
      <Provider store={store}>
        <App />
      </Provider>,
      document.getElementById('react')
    )

    //store.subscribe(() =>
    //  console.log(store.getState())
    //)

  } else {
    let data = JSON.parse(evt.data)
    dispatchEvent(data.value)
    //store.dispatch(addNewEvent(JSON.stringify(data.value)))
  }
  //websocket.close();
}
function onError(evt)
{
  writeToScreen('<span style="color: red;">ERROR:</span> ' + evt.data);
}
function doSend(message)
{
  writeToScreen("SENT: " + message);
  websocket.send(message);
}
function writeToScreen(message)
{
  var pre = document.createElement("p");
  pre.style.wordWrap = "break-word";
  pre.innerHTML = message;
  output.appendChild(pre);
}
window.addEventListener("load", init, false);
